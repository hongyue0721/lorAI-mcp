# LorAI MCP 安装指南

本文档供 AI 智能体阅读并自动执行。如果你是人类，直接把 README 里的提示词发给你的智能体即可。

---

## 前置条件检查

### 1. Python 3.10+

```bash
python --version
```

### 2. .NET Framework 4.7.2+

检查注册表（Windows）：

```bash
reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release
```

Release 值 >= 528040 表示 4.8+（满足要求）。如果用户只需下载预编译 DLL 则不需要 .NET。

### 3. 游戏已安装

搜索游戏路径（不要假设默认 C 盘，游戏可能在 D 盘或其他位置）：

```bash
# 尝试常见位置
ls "C:/Program Files (x86)/Steam/steamapps/common/Library Of Ruina"
ls "D:/steam/steamapps/common/Library Of Ruina"
# 或者搜索
find /d/ -maxdepth 4 -name "LibraryOfRuina.exe" 2>/dev/null
```

### 4. BaseMod 已安装

**重要**：BaseMod for Library of Ruina 通过修改 `Assembly-CSharp.dll` 工作，不会以独立文件夹形式出现在 Mods/ 目录中。

**检测方法**：检查游戏根目录是否有 `LaunchWithMod.bat` 文件：

```bash
ls "<游戏路径>/LaunchWithMod.bat"
```

如果存在 `LaunchWithMod.bat`，说明 BaseMod 已安装。**不要在 Mods/ 文件夹中寻找 BaseMod 目录，它不在那里。**

如果 `LaunchWithMod.bat` 不存在，告知用户需要先安装 [BaseMod](https://github.com/USay560828/LoRBaseMod)，然后停止。

### 5. 检查 Mod 是否已部署

```bash
ls "<游戏路径>/LibraryOfRuina_Data/Mods/LorAIHost/Assemblies/LorAIHost.dll"
ls "<游戏路径>/LibraryOfRuina_Data/Mods/LorAIHost/StageModInfo.xml"
```

如果文件已存在，跳过 Step 3。

---

## Step 1: 安装 Python MCP Server

```bash
pip install git+https://github.com/hongyue0721/lorAI-mcp.git
```

如果遇到权限问题，加 `--user`：

```bash
pip install --user git+https://github.com/hongyue0721/lorAI-mcp.git
```

### 验证安装

```bash
python -c "from lor_mcp.server import mcp; print('OK')"
```

---

## Step 2: 注册 MCP 到 AI 客户端

```bash
lor-mcp-setup
```

这个命令会自动检测已安装的 AI 客户端（kimi-code / Claude Code / Claude Desktop / Cursor / Windsurf / Cline / OpenCode），并写入对应的 MCP 配置。

OpenCode 的配置格式与其他客户端不同（使用 `mcp` 顶键 + `command` 数组格式），脚本会自动处理。

如果 `lor-mcp-setup` 命令找不到，尝试：

```bash
python -m lor_mcp.setup
```

可选参数：
- `lor-mcp-setup --list` — 查看检测结果
- `lor-mcp-setup --client kimi` — 只注册到指定客户端
- `lor-mcp-setup --all` — 注册到全部已知客户端
- `lor-mcp-setup --unregister` — 取消注册

注册完成后，告知用户需要重启 AI 客户端才能生效。

---

## Step 3: 部署 C# Mod 到游戏

**如果 `Mods/LorAIHost/Assemblies/LorAIHost.dll` 已存在，跳过此步骤。**

### 方式一：下载预编译 Release（推荐）

1. 从 [Releases 页面](https://github.com/hongyue0721/lorAI-mcp/releases) 下载最新的 `LorAIHost.dll` 和 `StageModInfo.xml`
2. 找到游戏安装目录
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
<GameDir>D:\steam\steamapps\common\Library Of Ruina</GameDir>
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

## Step 4: 启动游戏并验证

1. 使用 `LaunchWithMod.bat` 启动游戏（不要直接从 Steam 启动）
2. 等待游戏加载到标题画面
3. 在标题画面，如果 BaseMod 显示 Mod 管理器，确认 **LorAIHost** 已启用
4. 验证连接：

```bash
curl http://localhost:17127/health
```

预期输出：

```json
{"status":"ok","version":"1.0.0","requests":1}
```

如果返回连接失败：
- 确认游戏已通过 `LaunchWithMod.bat` 启动（不是直接从 Steam 启动）
- 确认 LorAIHost Mod 已启用
- 确认游戏已加载到标题画面（不是在加载画面）

---

## Step 5: 重启 AI 客户端

重启你的 AI 客户端（kimi-code / Claude Code / Cursor 等），让它加载新的 MCP 配置。

然后让智能体调用 `health_check` 工具确认 MCP 连通。

---

## 卸载

```bash
# 1. 取消 MCP 注册
lor-mcp-setup --unregister

# 2. 卸载 Python 包
pip uninstall lor-mcp-server

# 3. 可选：删除游戏目录中的 Mod 文件
# rm -rf "<游戏路径>/LibraryOfRuina_Data/Mods/LorAIHost"
```

---

## 故障排查

| 问题 | 解决方案 |
|---|---|
| `pip install` 失败 | 确认 Python 3.10+，尝试 `pip install --user` |
| `lor-mcp-setup` 命令找不到 | 用 `python -m lor_mcp.setup` 替代 |
| 找不到 BaseMod 目录 | BaseMod 不在 Mods/ 里，检查 `LaunchWithMod.bat` 是否存在 |
| `dotnet build` 失败 | 检查 csproj 中 GameDir 路径是否正确 |
| 游戏中 Mod 不显示 | 确认用 `LaunchWithMod.bat` 启动，不是直接 Steam 启动 |
| health check 连接失败 | 游戏必须运行且加载到标题画面 |
| Mod 启用后游戏崩溃 | 检查游戏版本是否为 1.1.0.6a13，查看 Player.log |
