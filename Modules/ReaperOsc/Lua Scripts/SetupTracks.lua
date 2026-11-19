-- SetupTracks.lua (AsioAudioUnity)
--
-- Creates or updates one REAPER track per ASIO Audio Source of the Unity scene: named like the source (after its Spat source number,
-- e.g. "3 · Violin", with the REAPER Controller's Number Track Names), with its ReaRoute input
-- (ReaRoute 3 for output channel 3), armed with input monitoring (and set not to record files), and with the Spat Revolution Send plugin
-- with Local Audio Path enabled.
-- The REAPER Controller's "Set Up REAPER Tracks" button writes the list to %APPDATA%\AsioAudioUnity\SetupTracks.txt
-- ("channel,channelCount,number,name|..."; number: the Spat source number, 0 for none), then runs this script over OSC
-- (/action/<this script's command ID> "file"). The older "channel,channelCount,name|..." format is read too.
-- (A list sent as the OSC value itself is used too, but long values don't reach the script, so Unity uses the file.)
-- Running it again updates the tracks it created (found by their output channel): it never duplicates or deletes tracks.
--
-- Install: run RegisterScripts.lua once (it loads this script and gives its command ID to Unity).
-- Or by hand: Actions > Show action list > New action > Load ReaScript..., select this file, then right-click it in the action list
-- > Copy selected action command ID, and paste the ID (e.g. _RS7d3c...) in the REAPER Controller's "Set Up Tracks Script ID" field.
--
-- Local Audio Path: the script enables it through the plugin's "Audio Enable" parameter. If it isn't found, set up the plugin
-- once by hand (Local Audio Path on, and any other setting), and save it as an FX chain named "AsioAudioUnity Spat Send"
-- (track FX window > FX > Save chain..., in REAPER's default FXChains folder): the script then adds that chain instead, with its settings.

local SPAT_SEND = "Spat Revolution - Send"
local SPAT_SEND_CHAIN = "AsioAudioUnity Spat Send.RfxChain"
local CHANNEL_KEY = "P_EXT:AsioAudioUnityChannel"

local chainPath = reaper.GetResourcePath() .. "/FXChains/" .. SPAT_SEND_CHAIN
local hasChain = reaper.file_exists(chainPath)

local LIST_FILE = (os.getenv("APPDATA") or "") .. "\\AsioAudioUnity\\SetupTracks.txt"

-- The list written by Unity: a first line with the time it was written (Unix seconds), then the list.
local function readListFile()
    local file = io.open(LIST_FILE, "r")
    if file == nil then return nil, nil end
    local written = tonumber(file:read("l") or "")
    local content = file:read("a") or ""
    file:close()
    content = content:gsub("[\r\n]", "")
    return content, written
end

local _, _, _, _, _, _, _, context = reaper.get_action_context()
local list = context and context:match(":s=([^%]]+)")
local source = "the OSC message"
if list == nil or list == "" or list == "file" then
    local written
    list, written = readListFile()
    source = LIST_FILE .. (written and (", written " .. math.max(0, os.time() - written) .. " s ago") or "")
end
if list == nil or list == "" then
    reaper.ShowConsoleMsg("AsioAudioUnity SetupTracks: no track list received (looked in " .. LIST_FILE .. "; action context: \"" .. tostring(context) .. "\").\n"
        .. "Run it from Unity: REAPER Controller > Set Up REAPER Tracks.\n")
    return
end

-- ReaRoute inputs, by ReaRoute number. REAPER numbers its ReaRoute (loopback) inputs from 512 (I_RECINPUT: 512 = ReaRoute 1),
-- whatever the audio device (even Dummy Audio, which has no inputs of its own). If the audio device itself lists inputs named
-- "ReaRoute N", those are used instead.
local REAROUTE_FIRST_INPUT = 512

local function findReaRouteInputs()
    local inputs = {}
    for i = 0, reaper.GetNumAudioInputs() - 1 do
        local name = reaper.GetInputChannelName(i) or ""
        local number = name:match("ReaRoute%s*(%d+)")
        if number then inputs[tonumber(number)] = i end
    end
    return setmetatable(inputs, { __index = function(_, number)
        if type(number) ~= "number" or number < 1 or number > 512 then return nil end
        return REAROUTE_FIRST_INPUT + number - 1
    end })
end

-- A track created by this script for an output channel, or nil
local function findTrack(channel)
    for i = 0, reaper.CountTracks(0) - 1 do
        local track = reaper.GetTrack(0, i)
        local found, value = reaper.GetSetMediaTrackInfo_String(track, CHANNEL_KEY, "", false)
        if found and tonumber(value) == channel then return track end
    end
    return nil
end

local created, updated, missingInputs, missingPlugin = 0, 0, {}, false
local localPathSet, localPathUnknown, fromChain = 0, 0, 0

-- Enables Local Audio Path through the plugin's "Audio Enable" parameter (or one named like "Local Audio Path" in other versions).
-- Returns false if no such parameter was found.
local function enableLocalAudioPath(track, fx)
    local fallback = nil
    for p = 0, reaper.TrackFX_GetNumParams(track, fx) - 1 do
        local _, paramName = reaper.TrackFX_GetParamName(track, fx, p, "")
        local lower = (paramName or ""):lower()
        if lower:find("audio") and lower:find("enable") then
            reaper.TrackFX_SetParamNormalized(track, fx, p, 1)
            return true
        end
        if fallback == nil and lower:find("local") and (lower:find("audio") or lower:find("path")) then fallback = p end
    end
    if fallback ~= nil then
        reaper.TrackFX_SetParamNormalized(track, fx, fallback, 1)
        return true
    end
    return false
end

-- Adds the Spat Revolution Send plugin if the track doesn't have it yet (from the saved FX chain if there is one), with Local Audio Path.
local function setUpSpatSend(track)
    local fx = reaper.TrackFX_AddByName(track, SPAT_SEND, false, 0) -- already there?
    local added = false
    if fx < 0 and hasChain then
        reaper.TrackFX_AddByName(track, SPAT_SEND_CHAIN, false, -1)
        fx = reaper.TrackFX_AddByName(track, SPAT_SEND, false, 0)
        if fx >= 0 then fromChain = fromChain + 1; added = true end
    end
    if fx < 0 then fx = reaper.TrackFX_AddByName(track, SPAT_SEND, false, 1) end
    if fx < 0 then missingPlugin = true; return end
    if enableLocalAudioPath(track, fx) then
        localPathSet = localPathSet + 1
    elseif not added then
        localPathUnknown = localPathUnknown + 1 -- the chain sets it; otherwise it has to be checked by hand
    end
end

local function setUpTrack(channel, name, input, stereo)
    local track = findTrack(channel)
    if track == nil then
        reaper.InsertTrackAtIndex(reaper.CountTracks(0), true)
        track = reaper.GetTrack(0, reaper.CountTracks(0) - 1)
        reaper.GetSetMediaTrackInfo_String(track, CHANNEL_KEY, tostring(channel), true)
        created = created + 1
    else
        updated = updated + 1
    end

    reaper.GetSetMediaTrackInfo_String(track, "P_NAME", name, true)
    if input ~= nil then
        if stereo then
            reaper.SetMediaTrackInfo_Value(track, "I_NCHAN", 2)
            reaper.SetMediaTrackInfo_Value(track, "I_RECINPUT", 1024 + input) -- stereo input pair
        else
            reaper.SetMediaTrackInfo_Value(track, "I_RECINPUT", input)
        end
    else
        table.insert(missingInputs, channel)
    end
    reaper.SetMediaTrackInfo_Value(track, "I_RECARM", 1)  -- armed
    reaper.SetMediaTrackInfo_Value(track, "I_RECMON", 1)  -- input monitoring on
    reaper.SetMediaTrackInfo_Value(track, "I_RECMODE", 2) -- record: none (monitoring only, no file is recorded)

    setUpSpatSend(track)
end

local inputs = findReaRouteInputs()

reaper.Undo_BeginBlock()
reaper.PreventUIRefresh(1)

-- "3 · Violin": the Spat source number first, when there is one
local function numbered(number, name)
    if number ~= nil and number > 0 then return number .. " \u{00B7} " .. name end
    return name
end

for entry in list:gmatch("[^|]+") do
    local channel, count, number, name = entry:match("^(%d+),(%d+),(%d+),(.*)$")
    if channel == nil then
        channel, count, name = entry:match("^(%d+),(%d+),(.*)$") -- older format, without the number
    end
    channel, count, number = tonumber(channel), tonumber(count) or 1, tonumber(number) or 0
    if channel ~= nil then
        if name == nil or name == "" then name = "Channel " .. channel end
        if count == 2 then
            -- A stereo source: one stereo track on ReaRoute channel, channel + 1
            setUpTrack(channel, numbered(number, name), inputs[channel], inputs[channel] ~= nil and inputs[channel + 1] == inputs[channel] + 1)
        else
            for k = 0, math.max(1, count) - 1 do
                local trackName = count > 1 and (name .. " " .. (k + 1)) or name
                setUpTrack(channel + k, numbered(number > 0 and number + k or 0, trackName), inputs[channel + k], false)
            end
        end
    end
end

reaper.PreventUIRefresh(-1)
reaper.TrackList_AdjustWindows(false)
reaper.Undo_EndBlock("AsioAudioUnity: set up tracks", -1)

local message = "AsioAudioUnity: " .. (created + updated) .. " track(s) set up (" .. created .. " created, " .. updated .. " updated), from " .. source .. ".\n"
if #missingInputs > 0 then
    message = message .. "No ReaRoute input for channel(s) " .. table.concat(missingInputs, ", ") .. ".\n"
end
if missingPlugin then
    message = message .. "The Spat Revolution Send plugin wasn't found: install it from Flux Center, and rescan the VST plugins.\n"
end
if fromChain > 0 then
    message = message .. fromChain .. " Spat Revolution Send plugin(s) added from the \"AsioAudioUnity Spat Send\" FX chain.\n"
end
if localPathSet > 0 then
    message = message .. "Local Audio Path enabled in " .. localPathSet .. " Spat Revolution Send plugin(s).\n"
end
if localPathUnknown > 0 then
    message = message .. "No \"Audio Enable\" parameter found in the Spat Revolution Send plugin: check Local Audio Path in " .. localPathUnknown .. " plugin(s). "
        .. "To skip this next time, enable it in one plugin and save it as an FX chain named \"AsioAudioUnity Spat Send\" (FX > Save chain...).\n"
end
reaper.ShowConsoleMsg(message)
