# 废墟图书馆类型参考

> 基于实际运行时反射验证的类型信息。供 `callMethod` / `listMethods` 工具参考。

---

## 核心单例

| 类型 | 访问方式 | 用途 |
|---|---|---|
| `StageController` | `Singleton<StageController>.Instance` | 战斗流程控制 |
| `BattleObjectManager` | `BattleObjectManager.instance` | 战斗单位管理 |
| `UI.UIController` | `UI.UIController.Instance` | UI 导航控制 |
| `LibraryModel` | `LibraryModel.Instance` | 图书馆进度数据 |
| `GameSceneManager` | `GameSceneManager.Instance` | 场景管理 |
| `InventoryModel` | `Singleton<InventoryModel>.Instance` | 卡牌库存 |
| `BookInventoryModel` | `Singleton<BookInventoryModel>.Instance` | 书籍库存 |
| `DropBookInventoryModel` | `Singleton<DropBookInventoryModel>.Instance` | 邀请书库存 |
| `StageClassInfoList` | `Singleton<StageClassInfoList>.Instance` | 关卡数据 |

> 注意：全局命名空间有一个 `UIController`（光照控制器），UI 控制器在 `UI` 命名空间。`ReflectionHelper.FindType` 优先返回有命名空间的类型。

---

## StageController 关键方法

| 方法 | 说明 |
|---|---|
| `SkipRoundStartUI()` | 跳过回合开始动画（RoundStartPhase_UI） |
| `StopSpeedDiceRoll()` | 推进速度骰掷骰（RoundStartPhase_System） |
| `SetAutoCardForPlayer()` | 自动出牌 |
| `CompleteApplyingLibrarianCardPhase(bool)` | 确认出牌 |
| `StartBattle()` | 开始战斗 |
| `EndBattle()` | 结束战斗 |
| `CloseBattleScene()` | 关闭战斗场景 |
| `KillAllEnemy()` | 秒杀全部敌人 |

---

## StageController.StagePhase 枚举

```
RoundStartPhase_UI          ← 回合开始动画
RoundStartPhase_System      ← 速度骰掷骰
SortUnitPhase               ← 单位排序
DrawCardPhase               ← 抽牌
ApplyEnemyCardPhase         ← 敌人装卡
ApplyLibrarianCardPhase     ← 玩家装卡
ArrangeEquippedCards        ← 卡牌排序
ActivateStartBattleEffect   ← 开战效果
WaitStartBattleEffect
SetCurrentDiceAction
CheckFarAreaPlay
ExecuteFarAreaPlay
EndFarAreaPlay
MoveUnits
WaitUnitsArrive
CheckParrying               ← 拼点
CheckOneSideAction          ← 单方面行动
ProcessViewAction           ← 拼点演出
RoundEndPhase               ← 回合结束
EndBattle                   ← 战斗结束
EndBattle2
```

---

## BattleUnitModel 关键属性

| 属性/字段 | 类型 | 说明 |
|---|---|---|
| `index` | int | 单位索引 |
| `id` | int | 单位 ID |
| `faction` | Faction | Player / Enemy |
| `hp` | float | 当前 HP |
| `MaxHp` | int | 最大 HP |
| `breakDetail` | BreakDetail | 混乱护盾 |
| `emotionDetail` | BattleEmotionDetail | 情感详情 |
| `cardSlotDetail` | BattleCardSlotDetail | 出牌槽 |
| `speedDiceResult` | List<SpeedDice> | 速度骰 |
| `allyCardDetail` | BattleAllyCardDetail | 手牌管理 |
| `Book` | UnitBookModel | 装备书 |
| `UnitData` | UnitDataModel | 单位元数据 |

---

## 情绪卡 UI 类型

| 类型 | 访问方式 | 说明 |
|---|---|---|
| `BattleManagerUI` | `SingletonBehavior<BattleManagerUI>.Instance` | 战斗 UI 管理 |
| `ui_levelup` (field) | `BattleManagerUI.ui_levelup` | 情绪卡升级面板 |

`ui_levelup` 关键成员：
- `IsEnabled` (property) — 是否激活
- `candidates` (field, object[]) — 候选卡列表
- `OnSelectPassive(candidate)` — 选择一张卡
- `_needUnitSelection` (field, bool) — 是否需要选目标
- `OnClickTargetUnit(unit)` — 选择目标单位

---

## EmotionCardXmlInfo 字段

> 注意：这些是 public **field** 不是 property。使用 `GetMemberValue()` 统一查询。

| 字段 | 类型 |
|---|---|
| `id` | int |
| `Name` | string |
| `State` | enum (Positive/Negative) |
| `EmotionLevel` | int |
| `TargetType` | enum |

---

## DropBookXmlInfo vs BookXmlInfo

| 类型 | 用途 | HP 获取方式 |
|---|---|---|
| `DropBookXmlInfo` | 邀请书（库存列表） | 无 HP 字段 |
| `BookXmlInfo` | 实际书籍数据 | `BookXmlList.Instance.GetData(id).EquipEffect.Hp` |

选书时需要通过 `DropBookXmlInfo.id` 查 `BookXmlInfo` 获取 HP 排序。

---

## StoryRoot 类型

| 类型 | 访问方式 | 说明 |
|---|---|---|
| `StoryRoot` | `FindObjectOfType<StoryRoot>()` | 剧情根节点 |
| `storyManager` (field) | `StoryRoot.storyManager` | 剧情管理器 |

storyManager 方法：`SkipAll()`、`EndStory(bool)`、`ClickEvent(bool, bool)`
