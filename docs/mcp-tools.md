# LorAI MCP 工具一览

> MCP Server 通过 FastMCP 注册以下工具，AI 客户端可调用。
> 默认 profile 为 `guided`（由 `LOR_MCP_TOOL_PROFILE` 控制）。

---

## 读取工具（Read）

### `health_check`
检查 bridge 是否在线。

### `get_game_state`
完整游戏状态（navigation/battle/stages/inventory）。

### `get_state_layer(layer)`
获取指定层级状态。layer: `navigation` / `progression` / `floors` / `inventory` / `availablestages` / `battle`

### `get_battle_units`
所有战斗单位详情（HP/骰子/手牌/buff/情绪等级）。

### `get_emotion_candidates`
当前可选的情绪卡列表（index/name/state/level）。返回 `{active: false}` 表示情绪卡 UI 未激活。

### `get_stage_info(stage_id)`
指定关卡的 StageController 状态。

### `get_static_data_list`
列出 StaticDataExport 导出的静态数据文件。

### `get_game_data_item(collection, item_id)`
查询单个静态数据项。collection: `cards` / `books` / `enemies` / `passives` 等。

### `get_game_data_items(collection, item_ids)`
批量查询（逗号分隔 ID）。

### `get_action_status`
检查 deferred action 完成状态。

---

## 战斗工具（Write）

### `start_battle(stage_id, wait_seconds=40)`
启动指定关卡。流程：navigate Invitation → prepareBattle（自动选 HP 最高的书）→ startBattle → 等待战斗加载。

### `battle_loop(max_rounds=60)`
自动打完整场战斗。处理：
- 回合开始（SkipRoundStartUI / StopSpeedDiceRoll）
- 出牌（playBattleRound 原子操作）
- 情绪卡选择（正面优先 > 等级高优先）
- 卡住检测（phase 振荡检测 + forceAdvancePhase）
- 剧情跳过

返回 `{result: "victory"|"defeat"|"ended"|"timeout", rounds: N}`。

### `play_round()`
手动打一轮。等待 ApplyLibrarianCardPhase 后执行 playBattleRound。

### `select_emotion_card(index)`
按候选索引选情绪卡。自动处理 SelectOne 目标选择。

---

## 导航工具（Write）

### `start_game(wait_seconds=30)`
标题画面点 Continue，等待主界面加载。

### `navigate(phase)`
导航 UI 界面。phase: `Sephirah` / `Invitation` / `BattleSetting` / `Sepiroth` 等。

### `skip_story()`
跳过当前剧情。

### `click_battle_result()`
点击战斗结算按钮。

### `close_battle_scene()`
关闭战斗场景回到主界面。

---

## 通用工具

### `act(action, params="")`
通用 action 分发器。可调用所有底层 C# action。params 为逗号分隔的 key=value 对。

---

## 调试工具（Debug）

### `list_methods(object_type)`
列出游戏对象类型的所有 public 方法。

### `call_method(object_type, method_name, args="")`
反射调用游戏方法。args 为 JSON 数组字符串。

### `kill_all_enemy()`
秒杀全部敌人（测试用）。

---

## 环境变量

| 变量 | 默认值 | 说明 |
|---|---|---|
| `LOR_API_BASE_URL` | `http://localhost:17127` | 游戏 bridge 地址 |
| `LOR_PROXY_FALLBACK_URL` | `http://localhost:17128` | Python 代理回退 |
| `LOR_MCP_TOOL_PROFILE` | `guided` | 工具 profile（当前只支持 `guided`） |
