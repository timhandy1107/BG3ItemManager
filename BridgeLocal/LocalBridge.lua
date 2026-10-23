-- BG3 Item Manager mod-free local bridge.
-- Run via the Script Extender SERVER console; never package as a BG3 mod.
-- The external Windows application communicates through Ext.IO's private filesystem.
-- No arbitrary code execution: only three allow-listed commands are accepted.

local folder = "BG3ItemManager/"
local previousId = nil
local lastPoll = 0
local lastHeartbeat = 0
local loadedSession = false

local function safeName(character)
    local ok, name = pcall(function() return Osi.GetDisplayName(character) end)
    if ok and type(name) == "string" and name ~= "" then return name end
    return tostring(character):sub(1, 50)
end

local function getParty()
    local chars = {}
    local seen = {}
    local function insert(character)
        if type(character) == "string" and character ~= "" and not seen[character] then
            seen[character] = true
            chars[#chars + 1] = { id = character, name = safeName(character) }
        end
    end
    local ok, host = pcall(function() return Osi.GetHostCharacter() end)
    if ok then insert(host) end
    local partyOk, members = pcall(function() return Osi.DB_PartyMembers:Get(nil) end)
    if partyOk and type(members) == "table" then
        for _, member in ipairs(members) do
            if type(member) == "table" then insert(member[1]) end
        end
    end
    table.sort(chars, function(a, b) return a.name < b.name end)
    return chars
end

-- Loading this script from the console after a save is open skips SessionLoaded.
-- Detect an already available host at first initialization.
loadedSession = #getParty() > 0

local function isValidUuid(value)
    return type(value) == "string" and #value == 36 and
        value:match("^[0-9a-fA-F]+%-[0-9a-fA-F]+%-[0-9a-fA-F]+%-[0-9a-fA-F]+%-[0-9a-fA-F]+$") ~= nil
end

local function isPartyCharacter(id, chars)
    for _, char in ipairs(chars) do
        if char.id == id then return true end
    end
    return false
end

local function reply(id, ok, message, characters)
    local response = {
        id = id,
        ok = ok,
        message = message,
        characters = characters or {}
    }
    Ext.IO.SaveFile(folder .. "response.json", Ext.Json.Stringify(response))
end

local function process(command)
    if type(command) ~= "table" or type(command.id) ~= "string" or
       #command.id > 64 or command.version ~= 1 then return end
    if command.id == previousId then return end
    previousId = command.id
    -- Old command files must never trigger a spawn when BG3 is restarted.
    local currentEpoch = tonumber(Ext.Timer.ClockEpoch())
    if type(command.issuedAt) ~= "number" or not currentEpoch or
       math.abs(currentEpoch - command.issuedAt) > 12 then
        reply(command.id, false, "Abgelaufener oder ungültiger Befehl.")
        return
    end
    if command.action == "ping" then
        reply(command.id, loadedSession, loadedSession and "BG3 Lua-Brücke aktiv; Spielsitzung geladen." or "BG3 Lua-Brücke aktiv; noch kein Spielstand geladen.")
        return
    end
    if not loadedSession then
        reply(command.id, false, "Bitte zuerst einen BG3-Spielstand laden.")
        return
    end
    local party = getParty()
    if command.action == "list_characters" then
        reply(command.id, true, tostring(#party) .. " Charaktere gefunden.", party)
        return
    end
    if command.action == "give_item" then
        local count = command.quantity
        if not isValidUuid(command.template) then
            reply(command.id, false, "Ungültige Template-UUID.")
        elseif type(count) ~= "number" or count % 1 ~= 0 or count < 1 or count > 99 then
            reply(command.id, false, "Menge muss zwischen 1 und 99 liegen.")
        elseif not isPartyCharacter(command.target, party) then
            reply(command.id, false, "Empfänger ist aktuell kein erkanntes Gruppenmitglied.")
        else
            local success, err = pcall(function()
                Osi.TemplateAddTo(command.template, command.target, count, 1)
            end)
            if success then
                -- Osiris TemplateAddTo returns no item ID/success indication.
                reply(command.id, true, "TemplateAddTo aufgerufen. Bitte Inventar prüfen.")
            else
                reply(command.id, false, "Osiris-Fehler: " .. tostring(err):sub(1, 180))
            end
        end
        return
    end
    reply(command.id, false, "Unbekannter Befehl.")
end

Ext.Events.SessionLoaded:Subscribe(function() loadedSession = true end)
Ext.Events.SessionLoading:Subscribe(function() loadedSession = false end)
Ext.Events.Tick:Subscribe(function()
    local now = Ext.Timer.MonotonicTime()
    if now - lastPoll < 250 then return end
    lastPoll = now
    if now - lastHeartbeat > 1000 then
        lastHeartbeat = now
        Ext.IO.SaveFile(folder .. "status.json", Ext.Json.Stringify({
            version = 1,
            ready = loadedSession,
            name = "BG3ItemManagerBridge"
        }))
    end
    -- Ext.IO.LoadFile may return nil before the desktop application writes a command.
    local okRead, raw = pcall(function() return Ext.IO.LoadFile(folder .. "request.json") end)
    if not okRead or type(raw) ~= "string" or raw == "" or #raw > 8192 then return end
    local okParse, command = pcall(function() return Ext.Json.Parse(raw) end)
    if not okParse then return end
    local okProcess, err = pcall(function() process(command) end)
    if not okProcess then
        if type(command) == "table" and type(command.id) == "string" then
            reply(command.id, false, "Lua-Brückenfehler: " .. tostring(err):sub(1, 180))
        end
    end
end)

-- Mark the request present at boot as handled. This prevents cross-save replays
-- even when the file was written just before the game was restarted.
do
    local ok, raw = pcall(function() return Ext.IO.LoadFile(folder .. "request.json") end)
    if ok and type(raw) == "string" then
        local parsedOk, command = pcall(function() return Ext.Json.Parse(raw) end)
        if parsedOk and type(command) == "table" and type(command.id) == "string" then
            previousId = command.id
        end
    end
end

Ext.Utils.Print("[BG3ItemManager] Lokale Konsolen-Bridge gestartet (keine PAK-Mod).")
