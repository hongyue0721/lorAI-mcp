# LorAI MCP 安装指南

本文档供 AI 智能体阅读并自动执行。如果你是人类，直接把 README 里的提示词发给你的智能体即可。

---

## 前置条件检查

1. 确认 Python 3.10+ 已安装：`python --version`
2. 确认 .NET Framework 4.7.2 Developer Pack 或 Visual Studio Build Tools 已安装
3. 确认 Steam 上已安装 *Library of Ruina*
4. 确认游戏内已安装 [BaseMod](https://github.com/USay560828/LoRBaseMod) Mod 加载器

如果任何条件不满足，告知用户需要安装什么，然后停止。

---

## Step 1: 安装 Python MCP Server

```bash
pip install git+https://github.com/hongyue0721/lorAI-mcp.git
```

如果用户想从源码安装：

```bash
git clone https://github.com/hongyue0721/lorAI-mcp.git
cd lorAI-mcp
pip install -e .
```

---

## Step 2: 注册 MCP 到 AI 客户端

```bash
lor-mcp-setup
```

这个命令会自动检测已安装的 AI 客户端（kimi-code / Claude Code / Claude Desktop / Cursor / Windsurf / Cline），并写入对应的 MCP 配置。

可选参数：
- `lor-mcp-setup --list` — 查看检测结果
- `lor-mcp-setup --client kimi` — 只注册到指定客户端
- `lor-mcp-setup --all` — 注册到全部已知客户端
- `lor-mcp-setup --unregister` — 取消注册

注册完成后，告知用户需要重启 AI 客户端才能生效。

---

## Step 3: 部署 C# Mod 到游戏

### 方式一：下载预编译 Release（推荐）

1. 从 [Releases 页面](https://github.com/hongyue0721/lorAI-mcp/releases) 下载最新的 `LorAIHost.dll` 和 `StageModInfo.xml`
2. 找到游戏安装目录（通常在 `Steam/steamapps/common/Library Of Ruina/`）
3. 创建目录结构并复制文件：

```
LibraryOfRuina_Data/
  Mods/
    LorAIHost/
      Assemblies/
        LorAIHost.dll      ← 复制到这里
      StageModInfo.xml      ← 复制到这里
```

### 方式二：从源码编译

```bash
git clone https://github.com/hongyue0721/lorAI-mcp.git
cd lorAI-mcp/LorAIHost
```

编辑 `LorAIHost.csproj`，将 `<GameDir>` 改为用户的游戏路径：

```xml
<GameDir>C:\Program Files (x86)\Steam\steamapps\common\Library Of Ruina</GameDir>
```

编译并部署：

```bash
dotnet build -c Release
```

将产物复制到游戏目录：

```bash
GAME_DIR="<用户的游戏路径>"
mkdir -p "$GAME_DIR/LibraryOfRuina_Data/Mods/LorAIHost/Assemblies"
cp bin/Release/LorAIHost.dll "$GAME_DIR/LibraryOfRuina_Data/Mods/LorAIHost/Assemblies/"
cp StageModInfo.xml "$GAME_DIR/LibraryOfRuina_Data/Mods/LorAIHost/"
```

---

## Step 4: 游戏内启用 Mod

1. 从 Steam 启动 Library of Ruina
2. 在标题画面，BaseMod 会显示 Mod 管理器
3. 找到 **LorAIHost**，将其启用
4. 重启游戏

---

## Step 5: 验证连接

```bash
curl http://localhost:17127/health
```

预期输出：

```json
{"status":"ok","version":"1.0.0","requests":1}
```

如果返回连接失败，检查：
- 游戏是否已启动并加载到标题画面
- LorAIHost Mod 是否在 BaseMod 中已启用
- 防火墙是否阻止了 localhost:17127

---

## Step 6: 重启 AI 客户端

重启你的 AI 客户端（kimi-code / Claude Code / Cursor 等），让它加载新的 MCP 配置。

然后让智能体调用 `health_check` 工具确认 MCP 连通。

---

## 卸载

```bash
# 1. 取消 MCP 注册
lor-mcp-setup --unregister

# 2. 卸载 Python 包
pip uninstall lor-mcp-server

# 3. 游戏内禁用 LorAIHost Mod（BaseMod 管理器中操作）

# 4. 可选：删除游戏目录中的 Mod 文件
# rm -rf "<游戏路径>/LibraryOfRuina_Data/Mods/LorAIHost"
```

---

## 故障排查

| 问题 | 解决方案 |
|---|---|
| `pip install` 失败 | 确认 Python 3.10+，尝试 `pip install --user` |
| `lor-mcp-setup` 命令找不到 | 确认 Python Scripts 目录在 PATH 中 |
| `dotnet build` 失败 | 检查 csproj 中 GameDir 路径是否正确 |
| 游戏中 Mod 不显示 | 确认 BaseMod 已正确安装，检查 StageModInfo.xml 格式 |
| health check 连接失败 | 游戏必须运行且 Mod 已启用 |
| Mod 启用后游戏崩溃 | 检查游戏版本是否为 1.1.0.6a13，查看 Player.log |
