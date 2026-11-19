-- RegisterScripts.lua (AsioAudioUnity)
--
-- Loads the other AsioAudioUnity scripts of this folder (PlayRegion.lua, SetupTracks.lua...) into REAPER's action list, and gives
-- their command IDs to Unity: they are written to %APPDATA%\AsioAudioUnity\ScriptIds.txt ("PlayRegion.lua=_RS..." lines), and the
-- REAPER Controller fills its script ID fields from there. No more copying command IDs by hand.
--
-- Run it once, after the REAPER Controller's "Install into REAPER":
-- Actions > Show action list > New action > Load ReaScript..., select this file (Scripts\AsioAudioUnity), then click Run.
-- Running it again is harmless: scripts already in the action list keep their command ID.

local _, thisFile = reaper.get_action_context()
local folder = (thisFile or ""):match("^(.*)[\\/]")
if folder == nil then
    reaper.ShowConsoleMsg("AsioAudioUnity RegisterScripts: couldn't find this script's folder.\n")
    return
end
local thisName = thisFile:match("([^\\/]+)$")

local outputFolder = (os.getenv("APPDATA") or "") .. "\\AsioAudioUnity"
local outputFile = outputFolder .. "\\ScriptIds.txt"

local lines, report, failed = {}, {}, {}
local index = 0
while true do
    local name = reaper.EnumerateFiles(folder, index)
    if name == nil then break end
    index = index + 1
    if name:lower():match("%.lua$") and name ~= thisName then
        local commandId = reaper.AddRemoveReaScript(true, 0, folder .. "\\" .. name, true)
        local named = commandId ~= 0 and reaper.ReverseNamedCommandLookup(commandId) or nil
        if named ~= nil and named ~= "" then
            if named:sub(1, 1) ~= "_" then named = "_" .. named end
            table.insert(lines, name .. "=" .. named)
            table.insert(report, "  " .. name .. ": " .. named .. "\n")
        else
            table.insert(failed, name)
        end
    end
end

local message = "AsioAudioUnity: " .. #lines .. " script(s) in the action list:\n" .. table.concat(report)
if #failed > 0 then
    message = message .. "Couldn't load: " .. table.concat(failed, ", ") .. ".\n"
end

reaper.RecursiveCreateDirectory(outputFolder, 0)
local file = io.open(outputFile, "w")
if file ~= nil then
    file:write(table.concat(lines, "\n") .. "\n")
    file:close()
    message = message .. "Their command IDs are in " .. outputFile .. ": the REAPER Controller fills its script ID fields from there "
        .. "(select it in Unity, or click Fill from REAPER).\n"
else
    message = message .. "Couldn't write " .. outputFile .. ": copy the command IDs above into the REAPER Controller by hand.\n"
end
reaper.ShowConsoleMsg(message)
