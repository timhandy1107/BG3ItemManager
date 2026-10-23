-- Pure Lua simulation for BridgeMod/BootstrapServer.lua; no game process required.
-- Usage (from repository root): lua Tests/BridgeLuaSimulation.lua
local files = {}
local time = 0
local called = 0
local handlers = {}
local registry = {}
local nextEncoded = 0
local function encode(table)
    nextEncoded = nextEncoded + 1
    local id = "encoded-" .. tostring(nextEncoded)
    registry[id] = table
    return id
end
local function event(name)
    return { Subscribe = function(_, fn) handlers[name] = fn end }
end
Ext = {
    Events = { Tick = event("Tick"), SessionLoaded = event("SessionLoaded"), SessionLoading = event("SessionLoading") },
    Timer = { MonotonicTime = function() return time end, ClockEpoch = function() return os.time() end },
    IO = {
        LoadFile = function(path) return files[path] end,
        SaveFile = function(path, content) files[path] = content; return true end
    },
    -- Only pass tables through in this simulation: transport JSON itself is tested in C#.
    Json = { Parse = function(x) return registry[x] end, Stringify = encode },
    Utils = { Print = function(...) end }
}
Osi = {
    GetHostCharacter = function() return "host-id" end,
    GetDisplayName = function(uuid) return uuid == "host-id" and "Host" or "Shadowheart" end,
    DB_PartyMembers = { Get = function() return { { "host-id" }, { "shadowheart-id" } } end },
    TemplateAddTo = function(template, target, quantity, notification)
        assert(type(template) == "string" and target == "shadowheart-id" and quantity == 2 and notification == 1)
        called = called + 1
    end
}
local paths = "BG3ItemManager/"
dofile("BridgeMod/Mods/BG3ItemManagerBridge/ScriptExtender/Lua/BootstrapServer.lua")
local function tick()
    time = time + 350
    handlers.Tick()
end
local function send(req)
    req.issuedAt = req.issuedAt or os.time()
    files[paths .. "request.json"] = encode(req)
    tick()
    local reply = registry[files[paths .. "response.json"]]
    assert(reply ~= nil, "Keine Antwort")
    assert(reply.id == req.id, "Falsche Antwort-ID")
    return reply
end
local ping = send { version = 1, id = "ping1", action = "ping" }
assert(not ping.ok, "Ping vor SessionLoaded muss not-ready sein")
handlers.SessionLoaded()
local charResponse = send { version = 1, id = "chars1", action = "list_characters" }
assert(charResponse.ok and #charResponse.characters == 2, "Gruppenmitglieder")
local uuid = "d32a68ff-3b6a-4d83-b0c4-0a2c44b93ea9"
local reject = send { version = 1, id = "reject1", action = "give_item", template = uuid,
    target = "unknown-player", quantity = 2 }
assert(not reject.ok and called == 0, "Nicht-Parteimitglied abweisen")
local success = send { version = 1, id = "give1", action = "give_item", template = uuid,
    target = "shadowheart-id", quantity = 2 }
assert(success.ok and called == 1, "TemplateAddTo exakt einmal aufrufen")
tick()
assert(called == 1, "Gleiche Request-ID darf nicht zweimal ausgeführt werden")
local rejectCount = send { version = 1, id = "reject2", action = "give_item", template = uuid,
    target = "shadowheart-id", quantity = 999 }
assert(not rejectCount.ok and called == 1, "Zu große Menge abweisen")
local stale = send { version = 1, id = "stale", action = "give_item", template = uuid,
    target = "shadowheart-id", quantity = 2, issuedAt = os.time() - 86400 }
assert(not stale.ok and called == 1, "Abgelaufene Requests abweisen")
print("PASS: Lua-Bridge-Simulation (Session, Party, Give, Deduplikation, Sicherheit)")
