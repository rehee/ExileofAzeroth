# 角色外观配置 WoWCharConfig（PLAN-0011）

每个 DQQ 角色的 WoW 外观存在角色 DTO 的字符串字段 `WoWCharConfig` 里（JSON）。游戏里用插件的 **Barber**（梳妆台）、**Transmog**（幻化）、**Skills**（技能皮肤）窗口编辑，Save 时通过 DQQ `ICharConfigService.UpdateCharConfigAsync(EnumGameClient.WOW, 角色 id, 配置)` 写回。

## 来源与默认
- 只读 DTO 的 `WoWCharConfig`。在线模式下角色列表接口（`Character/DTO`）不带这个字段，runtime 对每个角色再按 id 取（`Character/DTO/{id}`）。
- 空的配置 = 人类女性、长相全取 0、只穿 DQQ 装备、默认技能表现。
- 回放：只看录像种子里角色 DTO 的配置；没有就是人类女性 + 种子里的装备。战场上的人物在 `combat.begin` 后才按种子建，不会先显示自由模式的样子。

## Skins 开关
插件的 **Skins** 按钮只管**装备幻化**和**技能皮肤**：关掉后自由模式与回放都只显示 DQQ 装备和默认技能表现；种族、性别、长相（`look`）始终按配置。开关只在这次运行有效，插件每次进入世界时同步一次；打开任一编辑窗口时自动打开。

## 格式（版本 1）
```jsonc
{
  "v": 1,
  "look": { "race": 4, "gender": "female", "skin": 2, "face": 1, "hair": 4, "hairColor": 3, "facialHair": 0 },
  "transmog": {
    "mainHand": { "item": "Thunderfury" },                                 // 物品库（wow-items.json）的名字或 WoW 物品编号
    "offHand":  { "display": 23361, "type": "oneHand", "weapon": "sword" }, // 直接写外观，写法同 equipment.json 的 items
    "head":     { "hide": true }                                           // 不显示
  },
  "skills":    { "RighteousFireProfile": { "ring": "Consecration" } },     // 技能皮肤（地面模型库 groundModels 的名字）
  "durations": { "PowerWordShield": { "hero": 11426 } }                    // 持续效果皮肤
}
```
- `look.race`：WoW 种族 id 或名字（human orc dwarf nightelf undead tauren gnome troll）；`gender`：male / female。
- `transmog` 的键：DQQ 装备槽（MainHand、Body……）或 WoW 槽名（head shoulder back chest shirt tabard wrist hands waist legs feet mainHand offHand）。有幻化的槽直接用幻化（不管 DQQ 那个槽穿了什么），没幻化的照常显示 DQQ 装备。幻化的武器种类决定战斗动作（弓 → 射击，魔杖 → 匕首站姿 + 魔杖射击）。
- `skills` 的键：DQQ 技能（EnumSkillNumber）；值可写 `action`、`spell`、`area`、`ray`、`ring`。`ring` 套到这个技能产生的持续效果上（`skills.json` 的 `skillDurations`）。
- 写错的项跳过并记日志，其余照用。

## 时装 cosmetics（预留，未实现）
在幻化之外加时装（用户 2026-11-11：火焰头、夸张的火焰翅膀……）。现在只定格式：runtime 读配置时跳过它，编辑窗口保存时原样保留，所以先写进去也不会丢。

```jsonc
"cosmetics": {
  "overhead": { "id": "FireHead" },     // 头顶 / 头部特效
  "back":     { "id": "FlameWings" },   // 背后（翅膀）
  "body":     { "id": "FrostOutfit" }   // 夸张服装
}
```
- 槽位是时装自己的一套，与装备槽分开：`overhead` `back` `body` `aura`（周身）`hands`……需要时再加。
- `id` 指向时装库 `config/mapping/cosmetics.json`（同物品库 wow-items.json 的思路）：角色配置只写 id，怎么画由时装库决定，改画法不用动角色数据。
- Skins 开关一起管时装：关掉皮肤，时装也不显示。
- **收费点（用户 2026-11-11）**：拥有哪些时装不能由配置字符串决定（它是客户端写的，谁都能写任意 id）。拥有记录放在 DQQ 服务器（例如账号或角色的已购时装列表）；DQQ 保存 WoWCharConfig 时检查 cosmetics 里的 id 是否已拥有，runtime 显示时也只画已拥有的（别人的录像同样按拥有记录过滤）。时装库的条目可加 `price`、`sku` 之类的字段供商店使用。

时装库的条目按 `kind` 选画法（由易到难，先用 WoW 客户端自带的资源，不分发暴雪资源）：
```jsonc
{
  "items": {
    // 1. aura：常驻法术特效挂在身上（同正义之火的光环机制），火焰头、周身光芒、脚下法阵
    "FireHead":    { "kind": "aura",  "spell": 0 },
    // 2. model：客户端已有的模型挂到身体挂点（头顶、背后、双手、肩……），可缩放、偏移、染色；火焰翅膀
    "FlameWings":  { "kind": "model", "model": "Spells\\xxx.m2", "attach": "back", "scale": 1.5, "offset": [0, 0, 0.2] },
    // 3. item：NPC 专用或放大的装备外观、换色，夸张服装
    "FrostOutfit": { "kind": "item",  "slot": "chest", "display": 0, "tint": [0.6, 0.8, 1.0] }
    // 4. 以后：自制模型 / 贴图放在项目自己的资源目录，客户端从那里读（自己的美术，可以分发）
  }
}
```
实现时从 1（aura）开始（现成机制，基本是配表）；2 要在客户端加“任意模型挂到挂点”；3 的染色要客户端支持；4 要能读外部资源。编辑界面在幻化窗口加一组时装槽。

## 编辑窗口
- **Barber**：种族、性别、肤色、脸、发型、发色、胡子；范围按客户端的建角数据。预览在玩家面前的替身（Preview）上：长相细节原地换装（同换装备，不拆模型），种族、性别原地重建；保存后自己的人物同样原地更新。
- **Transmog**：13 个槽，外观列表由客户端从本机 `ItemDisplayInfo.dbc` 按模型/贴图名整理（约 1.2 万种，同一外观只列一次；1.12 客户端没有物品名，显示模型名），物品库的条目带 `*` 排在最前。单手武器主副手都能选，弓、枪、魔杖在主手。Hide Slot = 不显示，Use Gear = 回到 DQQ 装备。
- **Skills**：能换范围圈的技能（`skillDurations` 里、持续效果有范围圈的），选项是地面模型库；只在战斗回放里看得见。
- 三个窗口共用一份正在改的配置；Save 写回 DQQ 后按 id 重新取角色再更新人物，Cancel 或关掉最后一个窗口丢掉改动。

## 桥接消息
runtime：`ui.character.edit {characterId, change}`（预览）、`ui.character.save`、`ui.character.cancel`（回答都是只含该角色的角色列表）、`ui.wow.items`（物品库）、`ui.skill.skins {characterId}`、`ui.skins {skins}`（开关同步）；`ui.character.list` 可带 `skins`（开关并重新穿）。
插件 ↔ 客户端的消息见 `addons/ExileUI/ExileWardrobe.lua` 开头。
