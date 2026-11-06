-- Exile UI: the minimal addon of PLAN-0003 E. It talks to the Exile Runtime (DQQ) through the
-- client bridge with addon messages, prefix "EXILE", fields split by ";" ("|" is the chat escape
-- character). See docs/bridge-protocol.md section 5.
--   addon -> bridge: ping;<x>  replay.list  replay.play;<id>  replay.export;<id>
--                    replay.files  replay.file;<name>
--   bridge -> addon: pong;<x>  replay.begin;<n>  replay;<id>;<label>  replay.end
--                    file.begin;<n>  file;<name>;<label>  file.end  exported;<name>  error;<text>
-- Two lists: "Recent" (the server's recordings, each can be exported to a file) and "Files" (the
-- recording files in the replays folder, DQQ's export format, played from the file).
-- Labels are English for now: the enUS client's fonts have no CJK glyphs.

local PREFIX = "EXILE"
local ROWS = 10
local ROW_HEIGHT = 20

local mode = "recent" -- or "files"
local entries = {}
local total = 0

local function Send(msg)
  SendAddonMessage(PREFIX, msg, "PARTY")
end

local function Print(text)
  DEFAULT_CHAT_FRAME:AddMessage("|cff66ccffExile:|r " .. text)
end

local function Split(text)
  local out = {}
  local start = 1
  while true do
    local i = string.find(text, ";", start, true)
    if not i then
      table.insert(out, string.sub(text, start))
      return out
    end
    table.insert(out, string.sub(text, start, i - 1))
    start = i + 1
  end
end

local function MakeButton(name, parent, width, text, onClick)
  local b = CreateFrame("Button", name, parent, "UIPanelButtonTemplate")
  b:SetWidth(width)
  b:SetHeight(22)
  b:SetText(text)
  b:SetScript("OnClick", onClick)
  return b
end

-- The window: a title, Recent/Files tabs, a status line, up to ROWS entries, then the actions.
local frame = CreateFrame("Frame", "ExileUIFrame", UIParent)
frame:SetWidth(460)
frame:SetHeight(140 + ROWS * ROW_HEIGHT)
frame:SetPoint("CENTER", UIParent, "CENTER", 0, 40)
frame:SetBackdrop({
  bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background",
  edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
  tile = true, tileSize = 32, edgeSize = 32,
  insets = { left = 11, right = 12, top = 12, bottom = 11 },
})
frame:SetMovable(true)
frame:EnableMouse(true)
frame:RegisterForDrag("LeftButton")
frame:SetScript("OnDragStart", function() this:StartMoving() end)
frame:SetScript("OnDragStop", function() this:StopMovingOrSizing() end)
frame:SetFrameStrata("DIALOG")
frame:Hide()
tinsert(UISpecialFrames, "ExileUIFrame") -- Escape closes it

local title = frame:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
title:SetPoint("TOP", frame, "TOP", 0, -18)
title:SetText("Combat Replays")

local status = frame:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
status:SetPoint("TOPLEFT", frame, "TOPLEFT", 24, -72)
status:SetText("")

local RequestList -- forward

local recentTab = MakeButton("ExileUIRecentTab", frame, 100, "Recent", function()
  mode = "recent"
  RequestList()
end)
recentTab:SetPoint("TOPLEFT", frame, "TOPLEFT", 24, -42)
local filesTab = MakeButton("ExileUIFilesTab", frame, 100, "Files", function()
  mode = "files"
  RequestList()
end)
filesTab:SetPoint("LEFT", recentTab, "RIGHT", 8, 0)

local rows = {}
for i = 1, ROWS do
  local top = -88 - (i - 1) * ROW_HEIGHT
  local play = CreateFrame("Button", "ExileUIRow" .. i, frame, "UIPanelButtonTemplate")
  play:SetWidth(330)
  play:SetHeight(ROW_HEIGHT - 2)
  play:SetPoint("TOPLEFT", frame, "TOPLEFT", 24, top)
  play.index = i
  play:SetScript("OnClick", function()
    local e = entries[this.index]
    if not e then return end
    Print("playing " .. e.label)
    frame:Hide()
    if mode == "files" then
      Send("replay.file;" .. e.key)
    else
      Send("replay.play;" .. e.key)
    end
  end)
  play:Hide()

  local export = CreateFrame("Button", "ExileUIExport" .. i, frame, "UIPanelButtonTemplate")
  export:SetWidth(70)
  export:SetHeight(ROW_HEIGHT - 2)
  export:SetPoint("LEFT", play, "RIGHT", 6, 0)
  export:SetText("Export")
  export.index = i
  export:SetScript("OnClick", function()
    local e = entries[this.index]
    if not e then return end
    status:SetText("Exporting...")
    Send("replay.export;" .. e.key)
  end)
  export:Hide()

  rows[i] = { play = play, export = export }
end

local function Refresh()
  if mode == "files" then
    recentTab:UnlockHighlight()
    filesTab:LockHighlight()
  else
    filesTab:UnlockHighlight()
    recentTab:LockHighlight()
  end
  for i = 1, ROWS do
    local e = entries[i]
    if e then
      rows[i].play:SetText(e.label)
      rows[i].play:Show()
      if mode == "recent" then rows[i].export:Show() else rows[i].export:Hide() end
    else
      rows[i].play:Hide()
      rows[i].export:Hide()
    end
  end
end

RequestList = function()
  entries = {}
  Refresh()
  status:SetText("Loading...")
  if mode == "files" then
    Send("replay.files")
  else
    Send("replay.list")
  end
end

local refresh = MakeButton("ExileUIRefresh", frame, 90, "Refresh", function() RequestList() end)
refresh:SetPoint("BOTTOMLEFT", frame, "BOTTOMLEFT", 24, 18)

local latest = MakeButton("ExileUILatest", frame, 120, "New / Latest", function()
  frame:Hide()
  Send("replay.play;")
end)
latest:SetPoint("BOTTOM", frame, "BOTTOM", 0, 18)

local close = MakeButton("ExileUIClose", frame, 90, "Close", function() frame:Hide() end)
close:SetPoint("BOTTOMRIGHT", frame, "BOTTOMRIGHT", -24, 18)

local function Toggle()
  if frame:IsShown() then
    frame:Hide()
  else
    frame:Show()
    RequestList()
  end
end

-- A small button left of the minimap opens the window.
local open = MakeButton("ExileUIOpen", UIParent, 100, "Replays", Toggle)
open:SetHeight(24)
open:SetFrameStrata("HIGH")
open:SetPoint("TOPRIGHT", UIParent, "TOPRIGHT", -200, -40)

SLASH_EXILE1 = "/exile"
SlashCmdList["EXILE"] = function(msg)
  if msg and string.sub(msg, 1, 4) == "ping" then
    Send("ping;" .. (GetTime() or 0))
  else
    Toggle()
  end
end

-- The bridge's answers.
local events = CreateFrame("Frame")
events:RegisterEvent("CHAT_MSG_ADDON")
events:SetScript("OnEvent", function()
  if arg1 ~= PREFIX then
    return
  end
  local f = Split(arg2 or "")
  local verb = f[1]
  if verb == "pong" then
    Print("pong " .. (f[2] or "") .. " (round trip " ..
      string.format("%.0f", ((GetTime() or 0) - (tonumber(f[2]) or 0)) * 1000) .. " ms)")
  elseif verb == "replay.begin" or verb == "file.begin" then
    entries = {}
    total = tonumber(f[2]) or 0
  elseif verb == "replay" or verb == "file" then
    table.insert(entries, { key = f[2] or "", label = f[3] or "" })
  elseif verb == "replay.end" then
    if total == 0 then
      status:SetText("No recordings yet. \"New / Latest\" starts a new combat.")
    else
      status:SetText(total .. " recordings, newest first. Click to watch, Export to save a file.")
    end
    Refresh()
  elseif verb == "file.end" then
    if total == 0 then
      status:SetText("No replay files. Put dqq-replay-*.txt files in the replays folder.")
    else
      status:SetText(total .. " replay files, newest first. Click to watch.")
    end
    Refresh()
  elseif verb == "exported" then
    status:SetText("Exported " .. (f[2] or "") .. " to the replays folder.")
    Print("exported " .. (f[2] or ""))
  elseif verb == "error" then
    status:SetText("Error: " .. (f[2] or ""))
    Print("error: " .. (f[2] or ""))
  end
end)
