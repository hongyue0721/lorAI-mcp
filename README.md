# LorAI MCP

**让 AI 智能体玩《废墟图书馆》。**

通过 MCP 协议将游戏状态、战斗操作、UI 导航全部暴露给 LLM——AI 读血量、看手牌、选情绪卡、出牌打怪，全自动。

```
AI Agent  ←MCP→  Python Server  ←HTTP→  C# Mod (in-game)
```

---

## 安装

### 面向人类用户

复制以下提示并粘贴到你的 LLM 智能体（kimi-code、Claude Code、Cursor 等）：

> 按照以下安装指南安装和配置 LorAI MCP：
> https://raw.githubusercontent.com/hongyue0721/lorAI-mcp/refs/heads/master/docs/guide/installation.md
>
> 或者直接阅读安装指南——但我们强烈建议让智能体来处理。人会犯错，智能体不会。

### 面向 LLM 智能体

获取安装指南并按照说明操作：

```bash
curl -s https://raw.githubusercontent.com/hongyue0721/lorAI-mcp/refs/heads/master/docs/guide/installation.md
```

### 卸载

要移除 LorAI MCP：

> 按照 https://raw.githubusercontent.com/hongyue0721/lorAI-mcp/refs/heads/master/docs/guide/installation.md 中的「卸载」章节执行卸载步骤。

---

## 前置条件

| 依赖 | 说明 |
|---|---|
| Steam | 安装 *Library of Ruina* (1.1.0.6a13) |
| Python 3.10+ | MCP Server 运行环境 |
| .NET Framework 4.7.2 | C# Mod 编译（需 Developer Pack 或 VS Build Tools） |
| [BaseMod](https://github.com/USay560828/LoRBaseMod) | 游戏内 Mod 加载器 |

---

## 它能做什么

- **读取游戏状态** — 关卡列表、角色血量、手牌、骰子、buff、敌人信息
- **自动战斗** — AI 决策出牌、推进回合、处理情绪卡选择
- **UI 导航** — 选楼层、进邀请、开战、结算、跳剧情
- **静态数据查询** — 卡牌、书籍、敌人、被动能力的完整数据
- **反射调试** — 列出/调用任意游戏方法，深度控制

---

## 项目结构

```
lorAI-mcp/
├── lor_mcp/                   # Python MCP Server
│   ├── server.py              # FastMCP 入口 + 基础 tools
│   ├── tools.py               # Guided tools（战斗自动化）
│   ├── setup.py               # MCP 一键注册脚本
│   ├── proxy_server.py        # HTTP 代理回退
│   └── config.py              # 环境变量配置
├── LorAIHost/                 # C# Mod（HTTP bridge + 状态导出 + 战斗自动化）
│   ├── LorAIHostMod.cs        # ModInitializer 入口
│   ├── HttpServer.cs          # HTTP 服务（端口 17127）
│   ├── StateExporter.cs       # 游戏状态导出
│   ├── ActionHandler.cs       # Action 分发
│   ├── AdvancedActions.cs     # 选书/情绪卡/phase 推进
│   ├── BattleActions.cs       # 出牌/确认/结束战斗
│   └── ...
├── docs/guide/installation.md # 安装指南（给智能体读的）
└── pyproject.toml
```

---

## 配置

| 环境变量 | 默认值 | 说明 |
|---|---|---|
| `LOR_API_BASE_URL` | `http://localhost:17127` | 游戏 bridge 地址 |
| `LOR_PROXY_FALLBACK_URL` | `http://localhost:17128` | Python 代理回退地址 |
| `LOR_MCP_TOOL_PROFILE` | `guided` | 工具集（`guided` 加载战斗自动化） |
| `LOR_GAME_DATA_DIR` | *(Steam 默认路径)* | 代理服务器的游戏数据目录 |

---

## 许可证

[MIT](LICENSE)
