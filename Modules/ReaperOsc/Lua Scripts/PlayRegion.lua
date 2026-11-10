-- PlayRegion.lua
--
-- Plays a region in loop: sets the time selection to the region, turns repeat on and starts playback.
-- The region number comes from the OSC message that runs the script: the REAPER Controller's "Play Region"
-- command sends /action/<this script's command ID> with the region number as a string argument.
--
-- Install: Actions > Show action list > New action > Load ReaScript..., select this file.
-- Then right-click it in the action list > Copy selected action command ID, and paste the ID
-- (e.g. _RS7d3c...) in the REAPER Controller's "Play Region Script ID" field in Unity.

local function findRegion(regionNumber)
    local i = 0

    while true do
        local retval, isRegion, pos, regionEnd, name, markerNumber =
            reaper.EnumProjectMarkers(i)

        if retval == 0 then
            break
        end

        if isRegion and markerNumber == regionNumber then
            return pos, regionEnd
        end

        i = i + 1
    end

    return nil, nil
end

local function getRegionNumber(context)

    local value = context:match(":s=([^%]]+)")

    if value == nil then
      return 0
    end

    return tonumber(value)

end

-- Get the OSC argument
local isNew, name, section, command, relative, resolution, value, context = reaper.get_action_context()

local regionNumber = getRegionNumber(context)

-- Find region
local startTime, endTime = findRegion(regionNumber)

if not startTime then
    reaper.ShowConsoleMsg(
        "Region " .. regionNumber .. " not found\n"
    )
    return
end

-- Set time selection
reaper.GetSet_LoopTimeRange(
    true,       -- set
    false,      -- isLoop
    startTime,
    endTime,
    false
)

-- Move cursor to beginning of region
reaper.SetEditCurPos(
    startTime,
    true,
    false
)

-- Enable repeat
reaper.GetSetRepeat(1)

-- Start playback
reaper.Main_OnCommand(1007, 0)
