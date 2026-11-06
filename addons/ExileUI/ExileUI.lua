-- Exile UI: the minimal addon of PLAN-0003 E. It talks to the Exile Runtime (DQQ) through the
-- client bridge with addon messages, prefix "EXILE", fields split by ";" ("|" is the chat escape
-- character). See docs/bridge-protocol.md section 4.
--   addon -> bridge: ping;<x>  replay.list  replay.play;<id>
--   bridge -> addon: pong;<x>  replay.begin;<n>  replay;<id>;<label>  replay.end  error;<text>
-- Labels are English for now: the enUS client's fonts have no CJK glyphs.

local PREFIX = "EXILE"
local ROWS = 10
local ROW_HEIGHT = 20

local replays = {}
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

-- The window: a title, a status line, up to ROWS recordings, Refresh and Close.
local frame = CreateFrame("Frame", "ExileUIFrame", UIParent)
frame:SetWidth(420)
frame:SetHeight(110 + ROWS * ROW_HEIGHT)
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
status:SetPoint("TOPLEFT", frame, "TOPLEFT", 24, -44)
status:SetText("")

local rows = {}
for i = 1, ROWS do
  local b = CreateFrame("Button", "ExileUIRow" .. i, frame, "UIPanelButtonTemplate")
  b:SetWidth(372)
  b:SetHeight(ROW_HEIGHT - 2)
  b:SetPoint("TOPLEFT", frame, "TOPLEFT", 24, -60 - (i - 1) * ROW_HEIGHT)
  b:SetScript("OnClick", function()
    local r = replays[this.index]
    if r then
      Print("playing " .. r.label)
      frame:Hide()
      Send("replay.play;" .. r.id)
    end
  end)
  b.index = i
  b:Hide()
  rows[i] = b
end

local function Refresh()
  for i = 1, ROWS do
    local r = replays[i]
    if r then
      rows[i]:SetText(r.label)
      rows[i]:Show()
    else
      rows[i]:Hide()
    end
  end
end

local function RequestList()
  replays = {}
  Refresh()
  status:SetText("Loading...")
  Send("replay.list")
end

local refresh = CreateFrame("Button", "ExileUIRefresh", frame, "UIPanelButtonTemplate")
refresh:SetWidth(90)
refresh:SetHeight(22)
refresh:SetPoint("BOTTOMLEFT", frame, "BOTTOMLEFT", 24, 18)
refresh:SetText("Refresh")
refresh:SetScript("OnClick", RequestList)

local latest = CreateFrame("Button", "ExileUILatest", frame, "UIPanelButtonTemplate")
latest:SetWidth(120)
latest:SetHeight(22)
latest:SetPoint("BOTTOM", frame, "BOTTOM", 0, 18)
latest:SetText("New / Latest")
latest:SetScript("OnClick", function()
  frame:Hide()
  Send("replay.play;")
end)

local close = CreateFrame("Button", "ExileUIClose", frame, "UIPanelButtonTemplate")
close:SetWidth(90)
close:SetHeight(22)
close:SetPoint("BOTTOMRIGHT", frame, "BOTTOMRIGHT", -24, 18)
close:SetText("Close")
close:SetScript("OnClick", function() frame:Hide() end)

local function Toggle()
  if frame:IsShown() then
    frame:Hide()
  else
    frame:Show()
    RequestList()
  end
end

-- A small button left of the minimap opens the window.
local open = CreateFrame("Button", "ExileUIOpen", UIParent, "UIPanelButtonTemplate")
open:SetWidth(100)
open:SetHeight(24)
open:SetFrameStrata("HIGH")
open:SetPoint("TOPRIGHT", UIParent, "TOPRIGHT", -200, -40)
open:SetText("Replays")
open:SetScript("OnClick", Toggle)

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
  elseif verb == "replay.begin" then
    replays = {}
    total = tonumber(f[2]) or 0
  elseif verb == "replay" then
    table.insert(replays, { id = f[2] or "", label = f[3] or "" })
  elseif verb == "replay.end" then
    if total == 0 then
      status:SetText("No recordings yet. \"New / Latest\" starts a new combat.")
    else
      status:SetText(total .. " recordings, newest first. Click one to watch it.")
    end
    Refresh()
  elseif verb == "error" then
    status:SetText("Error: " .. (f[2] or ""))
    Print("error: " .. (f[2] or ""))
  end
end)
