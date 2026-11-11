-- Exile UI: the barber and transmog windows (PLAN-0011, user 2026-11-11). Every change is
-- previewed on the character at once (the runtime applies it to a working copy of the character
-- config); Save writes it to DQQ (WoWCharConfig), Cancel or closing the window drops it.
--   addon -> bridge: look.get  look.limits;<race>;<sex>  look.set;<race>;<sex>;<skin>;<face>;<hair>;<hairColor>;<facialHair>
--                    tm.open  tm.list;<slot>;<page>;<search>  tm.set;<slot>;<key|hide|clear>  edit.save  edit.cancel
--                    sk.list  sk.set;<skill>;<model|default>
--   bridge -> addon: look;<race>;<sex>;<skin>;<face>;<hair>;<hairColor>;<facialHair>
--                    limits;<race>;<sex>;<skins>;<faces>;<hairs>;<hairColors>;<facialHairs>
--                    tm.begin;<slot>;<page>;<pages>  tm;<key>;<icon>;<name>  tm.end
--                    sk.begin  sk;<skill>;<label>;<kind>;<current>;<default>;<option,option...>  sk.end
-- Labels are English: the enUS client's fonts have no CJK glyphs.

local PREFIX = "EXILE"
local TM_ROWS = 10

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

local function MakeWindow(name, width, height, titleText)
  local f = CreateFrame("Frame", name, UIParent)
  f:SetWidth(width)
  f:SetHeight(height)
  f:SetPoint("CENTER", UIParent, "CENTER", 0, 40)
  f:SetBackdrop({
    bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background",
    edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
    tile = true, tileSize = 32, edgeSize = 32,
    insets = { left = 11, right = 12, top = 12, bottom = 11 },
  })
  f:SetMovable(true)
  f:EnableMouse(true)
  f:RegisterForDrag("LeftButton")
  f:SetScript("OnDragStart", function() this:StartMoving() end)
  f:SetScript("OnDragStop", function() this:StopMovingOrSizing() end)
  f:SetFrameStrata("DIALOG")
  f:Hide()
  tinsert(UISpecialFrames, name) -- Escape closes it (and drops the edits, see OnHide)
  local t = f:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
  t:SetPoint("TOP", f, "TOP", 0, -18)
  t:SetText(titleText)
  return f
end

-- Edits not saved yet (both windows share one working copy in the runtime), and a race or sex
-- turned whose look waits for the new dials' ranges.
local dirty = false
local saving = false
local lookWaits = false

local function Save()
  saving = true
  dirty = false
  Send("edit.save")
end

local function Cancel()
  if dirty then
    dirty = false
    Send("edit.cancel")
  end
end

-- What the edits show: the character's own look (turns Skins on, see ExileUI.lua).
local function ShowSkins()
  if ExileUI_SetSkins then ExileUI_SetSkins(true) end
end

---------------------------------------------------------------------------------------------------
-- Barber
---------------------------------------------------------------------------------------------------

local RACES = { "Human", "Orc", "Dwarf", "Night Elf", "Undead", "Tauren", "Gnome", "Troll" }
local DIALS = {
  { key = "race", label = "Race" },
  { key = "sex", label = "Sex" },
  { key = "skin", label = "Skin" },
  { key = "face", label = "Face" },
  { key = "hair", label = "Hair Style" },
  { key = "hairColor", label = "Hair Color" },
  { key = "facialHair", label = "Facial Hair" },
}
local look = { race = 1, sex = 0, skin = 0, face = 0, hair = 0, hairColor = 0, facialHair = 0 }
local limits = { race = 8, sex = 2, skin = 10, face = 10, hair = 10, hairColor = 10, facialHair = 10 }

local barber = MakeWindow("ExileBarberFrame", 300, 110 + table.getn(DIALS) * 30, "Barber")
barber:ClearAllPoints()
barber:SetPoint("CENTER", UIParent, "CENTER", -360, 40)
local barberValues = {}

local function BarberShow()
  for i = 1, table.getn(DIALS) do
    local d = DIALS[i]
    local v = look[d.key]
    local text
    if d.key == "race" then
      text = RACES[v] or ("Race " .. v)
    elseif d.key == "sex" then
      if v == 1 then text = "Female" else text = "Male" end
    else
      text = (v + 1) .. " / " .. (limits[d.key] or 1)
    end
    barberValues[i]:SetText(text)
  end
end

local function SendLook()
  dirty = true
  Send("look.set;" .. look.race .. ";" .. look.sex .. ";" .. look.skin .. ";" .. look.face .. ";" ..
    look.hair .. ";" .. look.hairColor .. ";" .. look.facialHair)
end

local function Turn(key, step)
  if key == "race" then
    look.race = math.mod(look.race - 1 + step + 8, 8) + 1
  elseif key == "sex" then
    look.sex = 1 - look.sex
  else
    local n = limits[key] or 1
    if n < 1 then n = 1 end
    look[key] = math.mod(look[key] + step + n, n)
  end
  BarberShow()
  if key == "race" or key == "sex" then
    -- The dials' ranges differ by race and sex: the look is sent once they are known.
    lookWaits = true
    Send("look.limits;" .. look.race .. ";" .. look.sex)
  else
    SendLook()
  end
end

for i = 1, table.getn(DIALS) do
  local d = DIALS[i]
  local top = -50 - (i - 1) * 30
  local label = barber:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  label:SetPoint("TOPLEFT", barber, "TOPLEFT", 24, top - 4)
  label:SetText(d.label)
  local prev = MakeButton("ExileBarberPrev" .. i, barber, 26, "<", function() Turn(this.key, -1) end)
  prev.key = d.key
  prev:SetPoint("TOPLEFT", barber, "TOPLEFT", 120, top)
  local value = barber:CreateFontString(nil, "OVERLAY", "GameFontHighlight")
  value:SetWidth(90)
  value:SetPoint("LEFT", prev, "RIGHT", 4, 0)
  barberValues[i] = value
  local nextb = MakeButton("ExileBarberNext" .. i, barber, 26, ">", function() Turn(this.key, 1) end)
  nextb.key = d.key
  nextb:SetPoint("LEFT", value, "RIGHT", 4, 0)
end

local barberSave = MakeButton("ExileBarberSave", barber, 100, "Save", function()
  Save()
  barber:Hide()
end)
barberSave:SetPoint("BOTTOMLEFT", barber, "BOTTOMLEFT", 24, 18)
local barberCancel = MakeButton("ExileBarberCancel", barber, 100, "Cancel", function() barber:Hide() end)
barberCancel:SetPoint("BOTTOMRIGHT", barber, "BOTTOMRIGHT", -24, 18)
-- Closing the last open window drops what was not saved.
-- Is another edit window still open (then closing this one keeps the edits)?
local function OthersOpen(me)
  for _, name in ipairs({ "ExileBarberFrame", "ExileTransmogFrame", "ExileSkillSkinFrame" }) do
    local f = getglobal(name)
    if f and f ~= me and f:IsShown() then return true end
  end
  return false
end

barber:SetScript("OnHide", function()
  if not saving and not OthersOpen(barber) then Cancel() end
  saving = false
end)

---------------------------------------------------------------------------------------------------
-- Transmog
---------------------------------------------------------------------------------------------------

local SLOTS = {
  { key = "head", label = "Head" }, { key = "shoulder", label = "Shoulder" }, { key = "back", label = "Back" },
  { key = "chest", label = "Chest" }, { key = "shirt", label = "Shirt" }, { key = "tabard", label = "Tabard" },
  { key = "wrist", label = "Wrist" }, { key = "hands", label = "Hands" }, { key = "waist", label = "Waist" },
  { key = "legs", label = "Legs" }, { key = "feet", label = "Feet" },
  { key = "mainHand", label = "Main Hand" }, { key = "offHand", label = "Off Hand" },
}
local slot = 1
local page, pages = 0, 1
local looks = {}
local picked = {} -- slot key -> the name picked this session

local tm = MakeWindow("ExileTransmogFrame", 560, 120 + 13 * 24, "Transmog")
local tmStatus = tm:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
tmStatus:SetPoint("BOTTOMLEFT", tm, "BOTTOMLEFT", 140, 48)
tmStatus:SetWidth(390)
tmStatus:SetJustifyH("LEFT")
tmStatus:SetText("")

local search = CreateFrame("EditBox", "ExileTransmogSearch", tm, "InputBoxTemplate")
search:SetWidth(240)
search:SetHeight(20)
search:SetAutoFocus(false)
search:SetPoint("TOPLEFT", tm, "TOPLEFT", 150, -46)

local RequestLooks -- forward

local slotButtons = {}
local function ShowSlots()
  for i = 1, table.getn(SLOTS) do
    if i == slot then slotButtons[i]:LockHighlight() else slotButtons[i]:UnlockHighlight() end
    local p = picked[SLOTS[i].key]
    if p then slotButtons[i]:SetText(SLOTS[i].label .. " *") else slotButtons[i]:SetText(SLOTS[i].label) end
  end
end

for i = 1, table.getn(SLOTS) do
  local b = MakeButton("ExileTransmogSlot" .. i, tm, 100, SLOTS[i].label, function()
    slot = this.index
    page = 0
    ShowSlots()
    RequestLooks()
  end)
  b.index = i
  b:SetPoint("TOPLEFT", tm, "TOPLEFT", 24, -46 - (i - 1) * 24)
  slotButtons[i] = b
end

local rows = {}
for i = 1, TM_ROWS do
  local icon = tm:CreateTexture(nil, "ARTWORK")
  icon:SetWidth(20)
  icon:SetHeight(20)
  icon:SetPoint("TOPLEFT", tm, "TOPLEFT", 140, -74 - (i - 1) * 24)
  local b = MakeButton("ExileTransmogRow" .. i, tm, 370, "", function()
    local l = looks[this.index]
    if not l then return end
    local s = SLOTS[slot]
    picked[s.key] = l.name
    dirty = true
    tmStatus:SetText(s.label .. ": " .. l.name)
    Send("tm.set;" .. s.key .. ";" .. l.key)
    ShowSlots()
  end)
  b.index = i
  b:SetPoint("LEFT", icon, "RIGHT", 4, 0)
  b:Hide()
  icon:Hide()
  rows[i] = { button = b, icon = icon }
end

local pageText = tm:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
local prevPage = MakeButton("ExileTransmogPrev", tm, 26, "<", function()
  if page > 0 then
    page = page - 1
    RequestLooks()
  end
end)
prevPage:SetPoint("TOPLEFT", tm, "TOPLEFT", 140, -74 - TM_ROWS * 24)
pageText:SetPoint("LEFT", prevPage, "RIGHT", 6, 0)
local nextPage = MakeButton("ExileTransmogNext", tm, 26, ">", function()
  if page + 1 < pages then
    page = page + 1
    RequestLooks()
  end
end)
nextPage:SetPoint("LEFT", prevPage, "RIGHT", 90, 0)

local hide = MakeButton("ExileTransmogHide", tm, 90, "Hide Slot", function()
  local s = SLOTS[slot]
  picked[s.key] = "(hidden)"
  dirty = true
  tmStatus:SetText(s.label .. ": hidden")
  Send("tm.set;" .. s.key .. ";hide")
  ShowSlots()
end)
hide:SetPoint("LEFT", nextPage, "RIGHT", 20, 0)
local clear = MakeButton("ExileTransmogClear", tm, 90, "Use Gear", function()
  local s = SLOTS[slot]
  picked[s.key] = nil
  dirty = true
  tmStatus:SetText(s.label .. ": the DQQ gear")
  Send("tm.set;" .. s.key .. ";clear")
  ShowSlots()
end)
clear:SetPoint("LEFT", hide, "RIGHT", 6, 0)

local tmSave = MakeButton("ExileTransmogSave", tm, 100, "Save", function()
  Save()
  tm:Hide()
end)
tmSave:SetPoint("BOTTOMLEFT", tm, "BOTTOMLEFT", 24, 18)
local tmCancel = MakeButton("ExileTransmogCancel", tm, 100, "Cancel", function() tm:Hide() end)
tmCancel:SetPoint("BOTTOMRIGHT", tm, "BOTTOMRIGHT", -24, 18)
tm:SetScript("OnHide", function()
  if not saving and not OthersOpen(tm) then Cancel() end
  saving = false
  picked = {}
end)

local function ShowLooks()
  for i = 1, TM_ROWS do
    local l = looks[i]
    if l then
      rows[i].icon:SetTexture(l.icon)
      rows[i].icon:Show()
      rows[i].button:SetText(l.name)
      rows[i].button:Show()
    else
      rows[i].icon:Hide()
      rows[i].button:Hide()
    end
  end
  pageText:SetText((page + 1) .. " / " .. pages)
end

RequestLooks = function()
  looks = {}
  ShowLooks()
  local text = string.gsub(search:GetText() or "", "[;|]", " ")
  Send("tm.list;" .. SLOTS[slot].key .. ";" .. page .. ";" .. text)
end

search:SetScript("OnEnterPressed", function()
  this:ClearFocus()
  page = 0
  RequestLooks()
end)
search:SetScript("OnEscapePressed", function() this:ClearFocus() end)
local find = MakeButton("ExileTransmogFind", tm, 60, "Find", function()
  search:ClearFocus()
  page = 0
  RequestLooks()
end)
find:SetPoint("LEFT", search, "RIGHT", 6, 0)

---------------------------------------------------------------------------------------------------
-- Skill skins (only range circles for now: Righteous Fire)
---------------------------------------------------------------------------------------------------

local SK_ROWS = 8
local skills = {}
local sk = MakeWindow("ExileSkillSkinFrame", 400, 130 + SK_ROWS * 30, "Skill Skins")
sk:ClearAllPoints()
sk:SetPoint("CENTER", UIParent, "CENTER", 0, 40)
local skNote = sk:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
skNote:SetPoint("TOP", sk, "TOP", 0, -42)
skNote:SetText("Seen in combat replays (range circles of auras).")

local skRows = {}
local function SkillShow()
  for i = 1, SK_ROWS do
    local e = skills[i]
    local r = skRows[i]
    if e then
      r.label:SetText(e.label)
      local v = e.options[e.pick]
      if e.pick == 0 then v = "Default (" .. (e.default ~= "" and e.default or "none") .. ")" end
      r.value:SetText(v)
      r.label:Show() r.value:Show() r.prev:Show() r.next:Show()
    else
      r.label:Hide() r.value:Hide() r.prev:Hide() r.next:Hide()
    end
  end
end

local function SkillTurn(i, step)
  local e = skills[i]
  if not e then return end
  local n = table.getn(e.options) + 1 -- 0 is the default
  e.pick = math.mod(e.pick + step + n, n)
  SkillShow()
  dirty = true
  if e.pick == 0 then
    Send("sk.set;" .. e.skill .. ";default")
  else
    Send("sk.set;" .. e.skill .. ";" .. e.options[e.pick])
  end
end

for i = 1, SK_ROWS do
  local top = -64 - (i - 1) * 30
  local label = sk:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  label:SetPoint("TOPLEFT", sk, "TOPLEFT", 24, top - 4)
  local prev = MakeButton("ExileSkillPrev" .. i, sk, 26, "<", function() SkillTurn(this.index, -1) end)
  prev.index = i
  prev:SetPoint("TOPLEFT", sk, "TOPLEFT", 160, top)
  local value = sk:CreateFontString(nil, "OVERLAY", "GameFontHighlight")
  value:SetWidth(150)
  value:SetPoint("LEFT", prev, "RIGHT", 4, 0)
  local nextb = MakeButton("ExileSkillNext" .. i, sk, 26, ">", function() SkillTurn(this.index, 1) end)
  nextb.index = i
  nextb:SetPoint("LEFT", value, "RIGHT", 4, 0)
  skRows[i] = { label = label, value = value, prev = prev, next = nextb }
end

local skSave = MakeButton("ExileSkillSave", sk, 100, "Save", function()
  Save()
  sk:Hide()
end)
skSave:SetPoint("BOTTOMLEFT", sk, "BOTTOMLEFT", 24, 18)
local skCancel = MakeButton("ExileSkillCancel", sk, 100, "Cancel", function() sk:Hide() end)
skCancel:SetPoint("BOTTOMRIGHT", sk, "BOTTOMRIGHT", -24, 18)
sk:SetScript("OnHide", function()
  if not saving and not OthersOpen(sk) then Cancel() end
  saving = false
end)

---------------------------------------------------------------------------------------------------
-- Buttons beside the others (ExileUI.lua: Replays, Reload Gear, Skins) and the bridge's answers
---------------------------------------------------------------------------------------------------

local tmOpen = MakeButton("ExileUITransmog", UIParent, 90, "Transmog", function()
  if tm:IsShown() then tm:Hide() return end
  ShowSkins()
  tm:Show()
  ShowSlots()
  Send("tm.open")
  RequestLooks()
end)
tmOpen:SetHeight(24)
tmOpen:SetFrameStrata("HIGH")
tmOpen:SetPoint("RIGHT", ExileUISkins, "LEFT", -6, 0)

local barberOpen = MakeButton("ExileUIBarber", UIParent, 80, "Barber", function()
  if barber:IsShown() then barber:Hide() return end
  ShowSkins()
  barber:Show()
  Send("look.get")
end)
barberOpen:SetHeight(24)
barberOpen:SetFrameStrata("HIGH")
barberOpen:SetPoint("RIGHT", tmOpen, "LEFT", -6, 0)

local skOpen = MakeButton("ExileUISkillSkins", UIParent, 70, "Skills", function()
  if sk:IsShown() then sk:Hide() return end
  ShowSkins()
  skills = {}
  SkillShow()
  sk:Show()
  Send("sk.list")
end)
skOpen:SetHeight(24)
skOpen:SetFrameStrata("HIGH")
skOpen:SetPoint("RIGHT", barberOpen, "LEFT", -6, 0)

local events = CreateFrame("Frame")
events:RegisterEvent("CHAT_MSG_ADDON")
events:SetScript("OnEvent", function()
  if arg1 ~= PREFIX then
    return
  end
  local f = Split(arg2 or "")
  local verb = f[1]
  if verb == "look" then
    look.race = tonumber(f[2]) or 1
    look.sex = tonumber(f[3]) or 0
    look.skin = tonumber(f[4]) or 0
    look.face = tonumber(f[5]) or 0
    look.hair = tonumber(f[6]) or 0
    look.hairColor = tonumber(f[7]) or 0
    look.facialHair = tonumber(f[8]) or 0
    BarberShow()
  elseif verb == "limits" then
    limits.skin = tonumber(f[4]) or 1
    limits.face = tonumber(f[5]) or 1
    limits.hair = tonumber(f[6]) or 1
    limits.hairColor = tonumber(f[7]) or 1
    limits.facialHair = tonumber(f[8]) or 1
    -- A new race or sex: keep each dial inside its new range, then preview.
    for _, k in ipairs({ "skin", "face", "hair", "hairColor", "facialHair" }) do
      if look[k] >= limits[k] then
        look[k] = 0
      end
    end
    BarberShow()
    if lookWaits then
      lookWaits = false
      SendLook()
    end
  elseif verb == "tm.begin" then
    looks = {}
    page = tonumber(f[3]) or 0
    pages = tonumber(f[4]) or 1
  elseif verb == "tm" then
    table.insert(looks, { key = f[2] or "", icon = f[3] or "", name = f[4] or "" })
  elseif verb == "tm.end" then
    ShowLooks()
  elseif verb == "sk.begin" then
    skills = {}
  elseif verb == "sk" then
    local options = {}
    local list = f[7] or ""
    local start = 1
    while list ~= "" do
      local i = string.find(list, ",", start, true)
      if not i then
        table.insert(options, string.sub(list, start))
        break
      end
      table.insert(options, string.sub(list, start, i - 1))
      start = i + 1
    end
    local e = { skill = f[2] or "", label = f[3] or "", current = f[5] or "", default = f[6] or "", options = options, pick = 0 }
    for i = 1, table.getn(options) do
      if options[i] == e.current then e.pick = i end
    end
    table.insert(skills, e)
  elseif verb == "sk.end" then
    SkillShow()
  end
end)
