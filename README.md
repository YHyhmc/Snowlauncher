## Snowlauncher

一个基于WebView2，可高度自定义的Minecraft启动器


## 技术结构

| 组件 | 用途 |
| --- | --- |
| .NET 10 | 原生窗口、文件系统、下载与进程管理 |
| WebView2 | 承载静态前端并连接原生功能 |
| Vue 3 | 用户界面与静态资源构建 |


## 构建环境

- Windows 10 1809 x64 或更高版本。
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。
- Node.js `^20.19.0` 或 `>=22.12.0`。
- Microsoft Edge WebView2 Runtime。
- 与目标 Minecraft 版本匹配的 Java 。

## 从源代码构建

克隆仓库后，先构建 Vue 静态资源：

```powershell
cd WPCL\Frontend
npm ci
npm run build
```

再构建 Windows x64 应用：

```powershell
cd ..\..
dotnet build .\WPCL\WPCL.csproj -c Release -p:Platform=x64
```

输出目录：

```text
WPCL\bin\x64\Release\net10.0-windows10.0.17763.0\
```

运行 `Snowlauncher.exe` 即可启动。默认游戏目录为程序输出目录中的 `.minecraft`，也可以在设置页选择其他目录。


## 声明

Snowlauncher 是独立的第三方项目，不属于 Mojang Studios、Microsoft 或 Xbox，也未获得这些公司的官方认可。Minecraft、Microsoft、Xbox 及相关名称和商标归各自权利人所有。用户应遵守 [Minecraft EULA](https://www.minecraft.net/eula) 和 [Minecraft Usage Guidelines](https://aka.ms/mcusageguidelines)。

