-- Loaded by the desktop app through the Script Extender's debug console.
--
-- This is the "no mod installed" path: the app injects a two line chunk that
-- loads this file, and this file loads the very same modules the packaged mod
-- uses. The modules register a Tick listener and then talk to the app through
-- the normal bridge files, so both paths behave identically.
--
-- If the packaged mod is already active its code is already running, so this
-- loader steps aside.

local MOD_UUID = "b7a1e0c7-1f2a-4b3c-9d4e-5f6a7b8c9d0e"

local function modIsActive()
    local loaded = nil
    local ok = pcall(function() loaded = Osi.NRD_IsModLoaded(MOD_UUID) end)
    return ok and tonumber(loaded) == 1
end

if EocTrainer ~= nil and EocTrainer.RUNNING then
    return "already running"
end

if modIsActive() then
    return "packaged mod is active; console mode not needed"
end

local function loadModule(name)
    local path = "EocTrainer/lua/" .. name
    local source = Ext.IO.LoadFile(path)
    if source == nil then
        error("console loader: missing module " .. path)
    end

    local chunk = Ext.Utils.LoadString(source, "@" .. name)
    if chunk == nil then
        error("console loader: could not compile " .. name)
    end

    return chunk()
end

loadModule("Enums.lua")
loadModule("Bridge.lua")
loadModule("State.lua")
loadModule("Ops.lua")
loadModule("Main.lua")

return "console mode active"
