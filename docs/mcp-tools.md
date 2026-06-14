# LorAIAgent MCP 工具规范

> 面向 AI 客户端的工具接口定义。MCP Server 把这些工具映射到 Mod 的 HTTP API。

---

## 基础工具

### `health_check`

检查 Mod 是否在线。

```python
@tool
def health_check() -> dict[str, Any]:
    """Check whether the Library of Ruina AI Agent Mod is loaded and reachable."""
```

**返回**：
```json
{
  "ok": true,
  "service": "lor-ai-agent",
  "mod_version": "0.1.0",
  "protocol_version": "2025-06-11-v1",
  "game_version": "1.1.0.6b",
  "status": "ready"
}
```

---

### `get_game_state`

读取精简的 AI 视图状态。

```python
@tool
def get_game_state() -> dict[str, Any]:
    """Read the compact agent-facing game state snapshot."""
```

---

### `get_raw_game_state`

读取完整原始状态，用于调试。

```python
@tool
def get_raw_game_state() -> dict[str, Any]:
    """Read the full raw /state snapshot for debugging or schema inspection."""
```

---

### `get_available_actions`

当前可执行动作，含参数要求。

```python
@tool
def get_available_actions() -> list[dict[str, Any]]:
    """List currently executable actions with parameter requirement hints."""
```

---

## 元数据工具

### `get_game_data_item`

按 ID 查单个元数据。

```python
@tool
def get_game_data_item(collection: str, item_id: str) -> dict[str, Any] | None:
    """Return a single item from a game metadata collection by id.

    Example: get_game_data_item(collection='combat_pages', item_id='PAGE_STRIKE')
    """
```

### `get_game_data_items`

批量查元数据。

```python
@tool
def get_game_data_items(collection: str, item_ids: str) -> dict[str, Any]:
    """Return multiple items (by comma-separated ids) from a collection."""
```

### `get_relevant_game_data`

按当前场景返回精简字段，省 token。

```python
@tool
def get_relevant_game_data(collection: str, item_ids: str) -> dict[str, Any]:
    """Return items with only the most relevant fields for the current game context.

    Automatically detects scene (reception/abnormality/menu) and minimizes token usage.
    """
```

**支持的 collection**：
- `combat_pages`
- `key_pages`
- `guests`
- `abnormalities`
- `floors`
- `passives`
- `ego_pages`

---

## 等待工具

### `wait_for_event`

SSE 等待指定事件。

```python
@tool
def wait_for_event(event_names: str = "", timeout_seconds: float = 20.0) -> dict[str, Any]:
    """Wait for one matching game event from /events/stream.

    event_names: comma-separated event names. Empty means accept any event.
    timeout_seconds: maximum wait time before returning matched=false.
    """
```

### `wait_until_actionable`

等到 AI 可以决策的状态。

```python
@tool
def wait_until_actionable(timeout_seconds: float = 20.0) -> dict[str, Any]:
    """Wait until a new actionable phase is reported, then return fresh state.

    Reduces high-frequency polling between enemy turns, scene transitions,
    and clash resolutions. Falls back to polling when SSE is unavailable.
    """
```

---

## 统一动作入口

### `act`

废墟图书馆的战斗阶段复杂，统一用 `act` 执行所有动作。

```python
@tool
def act(
    action: str,
    unit_index: int | None = None,
    die_index: int | None = None,
    target_index: int | None = None,
    page_index: int | None = None,
    option_index: int | None = None,
) -> dict[str, Any]:
    """Execute one currently available game action.

    Usage loop:
    1. Call get_game_state() and get_available_actions().
    2. Branch on state.scene and state.phase.
    3. Pick an action that is currently available.
    4. Pass only the indices required by that action from the latest state.
    5. Read state again after the action completes.

    Parameters:
    - action: action name from available_actions.
    - unit_index: for actions involving a librarian/guest.
    - die_index: for actions involving a speed die.
    - target_index: for actions requiring a target.
    - page_index: for actions involving a combat page.
    - option_index: for menu/reward/selection actions.
    """
```

### 动作名称表

| 动作名 | 需要的参数 | 说明 |
|---|---|---|
| `assign_speed_die` | `unit_index`, `die_index`, `target_index` | 把速度骰分配给目标 |
| `equip_combat_page` | `unit_index`, `die_index`, `page_index` | 给速度骰装书页 |
| `change_clash_target` | `unit_index`, `die_index`, `target_index` | 改变拼点目标 |
| `use_ego_page` | `unit_index`, `option_index` | 使用 EGO |
| `burn_emotion_coin` | `unit_index`, `die_index`, `option_index` | 烧情感硬币 |
| `use_floor_ability` | `option_index` | 使用楼层能力 |
| `confirm_scene` | 无 | 确认当前幕 |
| `pass_dice` | `unit_index`, `die_index` | 放弃速度骰 |
| `select_reward` | `option_index` | 选择战后奖励 |
| `advance_dialog` | 无 | 推进对话 |
| `select_menu_option` | `option_index` | 选择菜单项 |
| `open_reception` | `option_index` | 开启接待 |
| `confirm_selection` | 无 | 确认当前选择 |

---

## Profile 系统

MCP Server 支持通过环境变量切换工具面：

```bash
set LOR_MCP_TOOL_PROFILE=guided    # 默认，最精简
set LOR_MCP_TOOL_PROFILE=layered   # 额外 handoff + 知识库工具
set LOR_MCP_TOOL_PROFILE=full      # 包含 legacy 单动作工具
```

### `guided`（推荐默认）

只暴露：
- `health_check`
- `get_game_state`
- `get_raw_game_state`
- `get_available_actions`
- `get_game_data_item`
- `get_game_data_items`
- `get_relevant_game_data`
- `wait_for_event`
- `wait_until_actionable`
- `act`

### `layered`

在 guided 基础上增加：
- `get_planner_context`
- `create_planner_handoff`
- `get_tactical_context`
- `create_tactical_handoff`
- `complete_tactical_handoff`
- `append_combat_knowledge`
- `append_guest_knowledge`

### `full`

再增加 legacy 单动作工具（用于兼容性测试）：
- `assign_speed_die`
- `equip_combat_page`
- `use_ego_page`
- ...

---

## 环境变量

| 变量 | 默认值 | 说明 |
|---|---|---|
| `LOR_API_BASE_URL` | `http://127.0.0.1:17127` | Mod HTTP 地址 |
| `LOR_API_READ_TIMEOUT` | `10` | 读状态超时（秒） |
| `LOR_API_ACTION_TIMEOUT` | `30` | 执行动作超时（秒） |
| `LOR_MCP_TOOL_PROFILE` | `guided` | 工具面级别 |
| `LOR_AGENT_KNOWLEDGE_DIR` | `./knowledge` | 知识库目录 |
