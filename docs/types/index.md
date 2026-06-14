# 废墟图书馆类型发现指南

> 基于 Unity Mono + C#，通过反射和 Harmony 补丁在游戏运行时探测类型。
> 所有类型名都需要在实际运行时验证，以下为先验推测。

---

## 一、核心类型速查

### 舞台与流程

| 概念 | 推测类型 | 关键成员 |
|---|---|---|
| 舞台控制器 | `StageController` | `Instance`, `currentStage`, `currentFloor`, `StartStage()`, `EndOfRound()` |
| 楼层模型 | `FloorModel` / `LibraryFloorModel` | `floorNum`, `Name`, `GetLibrarians()` |
| 接待关卡 | `StageClassInfo` / `StageModel` | `id`, `waveList`, `floorOnly` |

### 战斗单位

| 概念 | 推测类型 | 关键成员 |
|---|---|---|
| 战斗对象管理器 | `BattleObjectManager` | `instance`, `GetList()`, `GetAliveList()` |
| 战斗单位 | `BattleUnitModel` | `hp`, `breakGauge`, `bp`, `emotionDetail`, `cardSlotDetail`, `speedDiceResult`, `currentBehavior` |
| 友方司书 | `BattleAllyModel` : `BattleUnitModel` | 继承 BattleUnitModel |
| 敌方来宾 | `BattleEnemyModel` : `BattleUnitModel` | 继承 BattleUnitModel |

### 速度骰与书页

| 概念 | 推测类型 | 关键成员 |
|---|---|---|
| 速度骰 | `SpeedDice` / `BattleSpeedDice` | `value`, `isOn`, `isBlocked`, `isBreaked` |
| 战斗书页（卡牌） | `BattleDiceCardModel` | `id`, `name`, `cost`, `abilityList`, `behaviorList` |
| 骰子行为 | `BattleDiceBehavior` | `min`, `max`, `power`, `diceType`, `detail` |
| 手牌槽 | `BattleAllyCardSlotDetail` | `SetField()`, `GetField()`, `hand` |
| 当前行为 | `BattlePlayingCardDataInUnitModel` | `card`, `target`, `currentBehaviour` |

### 情感与楼层

| 概念 | 推测类型 | 关键成员 |
|---|---|---|
| 情感详情 | `BattleEmotionDetail` | `emotionLevel`, `emotionCoins`, `CreateEmotionCoin()` |
| 情感硬币 | `EmotionCoinModel` / `BattleEmotionCoinModel` | `positive`, `xmlInfo` |
| EGO 书页 | `EmotionCardModel` / `EgoCardModel` | `id`, `Name` |

### UI 与输入

| 概念 | 推测类型 | 关键成员 |
|---|---|---|
| 主动画状态机 | `UIAnimationManager` | - |
| 速度骰 UI | `SpeedDiceUI` | `SetSelectable()`, `SetData()` |
| 书页 UI | `BattleDiceCardUI` | `SetData()`, `OnClick()` |
| 对话框 | `DialogController` / `StoryManager` | `Next()`, `IsEnd()` |

---

## 二、发现方法

### 方法 1：启动时全 Assembly 扫描

```csharp
public static class TypeScanner
{
    public static void Scan()
    {
        var keywords = new[] { "Stage", "Battle", "Dice", "Card", "Emotion", "Floor", "Speed" };
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var type in asm.GetTypes())
                {
                    if (keywords.Any(k => type.Name.Contains(k)))
                    {
                        LogType(type);
                    }
                }
            }
            catch { }
        }
    }

    static void LogType(Type type)
    {
        Logger.LogInfo($"[TYPE] {type.FullName}");
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            Logger.LogInfo($"  FIELD: {f.FieldType.Name} {f.Name}");
        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var ps = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
            Logger.LogInfo($"  METHOD: {m.ReturnType.Name} {m.Name}({ps})");
        }
    }
}
```

### 方法 2：Harmony 关键方法 Hook

```csharp
[HarmonyPatch(typeof(StageController), "StartStage")]
public static class StageController_StartStage_Patch
{
    [HarmonyPostfix]
    static void Postfix(StageController __instance)
    {
        Logger.LogInfo("[HOOK] StageController.StartStage called");
        // 反射打印 __instance 的 public 字段值
    }
}
```

### 方法 3：单例类直接访问

图书馆里大量 Manager 是单例，常见模式：

```csharp
StageController.Instance
BattleObjectManager.instance
BookInventoryModel.Instance
UnitBookStoryModel.Instance
```

单例是最高效的入口点。

---

## 三、已知稳定的 Hook 点

以下方法在战斗中会被频繁调用，适合 Hook 做状态感知：

| 方法 | 触发时机 |
|---|---|
| `StageController.StartStage()` | 接待开始 |
| `StageController.EndOfRound()` | 一回合结束 |
| `StageController.EndBattle()` | 战斗结束 |
| `BattleUnitModel.OnRollSpeedDice()` | 速度骰掷出后 |
| `BattleUnitModel.OnSelectCard()` | 选卡后 |
| `BattleUnitModel.OnDie()` | 单位死亡 |
| `BattleDiceBehavior.GiveDamage()` | 造成伤害 |
| `BattleEmotionDetail.LevelUp()` | 情感升级 |

---

## 四、需要确认的关键问题

1. `StageController` 是不是单例？访问方式是 `Instance` 还是 `instance`？
2. `BattleObjectManager` 的单位列表字段名是什么？
3. 速度骰的结果是存在 `BattleUnitModel.speedDiceResult` 还是单独的对象里？
4. 手牌是 `cardSlotDetail.hand` 还是 `handSlot`？
5. 情感硬币的正面/负面是怎么表示的？
6. EGO 书页的选择是 UI 事件还是可以直接调用方法？
7. 确认当前幕操作对应哪个方法？

这些问题都要靠 Phase 1 的扫描和 Hook 来回答。
