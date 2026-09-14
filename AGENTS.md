# 项目协作记忆

## 项目定位

- `AvaloniauiForEasytier` 是 EasyTier 的跨平台桌面控制台，使用 Avalonia UI 和 SukiUI 构建。
- 程序必须同时适配 Linux 和 Windows；新增功能需要考虑两个平台的启动、路径、进程、权限、网络和窗口行为差异。
- 默认使用 .NET/Avalonia 的跨平台 API，避免直接依赖 Windows 专属 API、注册表、PowerShell、盘符路径或仅 Linux 可用的 Shell 命令；确需平台差异时必须隔离到平台适配层。
- 文件路径使用 `Path.Combine` 等跨平台 API，进程启动、系统托盘、开机启动和应用退出行为不得假设单一操作系统。
- 发布必须支持 Windows x64 与 Linux x64，并同时提供“单文件自包含”和“Native AOT 自包含”两种模式。Avalonia/Skia 的 AOT 输出可能包含本机图形旁置库；两个平台分别在对应操作系统上执行 AOT 发布，不假设可以跨操作系统编译。
- 这是桌面端软件，不按网页后台、营销站点或响应式 Web 页面设计。
- 桌面界面优先使用菜单栏、命令工具栏、紧凑侧边导航、可调整大小的工作区、表格、属性面板、输出窗口和状态栏。
- 当前窗口保留 SukiUI 标题栏，已移除应用自定义菜单栏和服务工具栏；除非用户明确要求，不新增透明自定义拖动区域。
- 避免网页式大标题、大面积留白、营销文案、巨大指标卡片和装饰性卡片堆叠。
- UI 页面应完整覆盖主页、网络配置、节点管理、路由管理、运行日志和应用设置，并提供真实的桌面页面导航。

## Avalonia 与 SukiUI

- 项目当前使用 Avalonia `12.0.4`。
- 项目使用 SukiUI `7.0.1`，不要降回 SukiUI `6.x`；SukiUI `6.x` 依赖 Avalonia 11，会导致 `Avalonia.Controls.Chrome.TitleBar` 类型加载异常。
- `MainWindow` 继承 `SukiUI.Controls.SukiWindow`，应用主题使用 `SukiUI.SukiTheme`。
- 当前项目对 XAML 字符串事件绑定存在运行时兼容问题。不要在窗口 XAML 中使用 `Click="方法名"`；给控件设置 `x:Name`，在代码后置构造函数中使用强类型事件订阅（例如 `Button.Click += Handler`）。
- 修改 Avalonia XAML 后必须进行编译检查，并尽量做一次实际启动检查。

## Visual Studio Git 提交说明

- 使用 Conventional Commits 规范。
- 提交说明使用简体中文描述，正文禁止中英文混杂。
- 每条变更必须以 `feat`、`fix`、`docs`、`style`、`refactor`、`test`、`chore` 等合规类型开头。
- 提交说明必须依据当前暂存区或实际代码变更生成，不能描述变更范围之外的内容。
- 一次提交包含多类变更时，可以使用多行提交说明，每行使用对应类型。

示例：

```text
feat: 增加用户登录功能
fix: 修复注册页面的表单验证问题
```

## WeightingWeb 接口规范

- 新增或修改接口返回 JSON 时，必须使用 `SysTools.ConvertToJson(匿名对象/Dictionary)` 序列化，禁止字符串拼接。
- `PlanController`、`LoginController` 中已有的字符串拼接代码不强制修改。
- `JsonResponse(str)` 定义于 `WeightingBillController`，其他 Controller 需自行添加后再使用。
- 示例：`return JsonResponse(SysTools.ConvertToJson(new { errCode = 0, errMsg = "成功", data = result }));`
- WeightingWeb 查询优先使用 Method Syntax，例如 `.Where().Select().OrderBy().ToList()`；非必要时不用 Query Syntax。WeightingSystem 不受此限制。

## 字段新增审批

- 新增 JSON 字段、模型属性、DTO 字段或数据库映射列前，必须先向用户确认字段名、类型和用途。
- 禁止擅自新增上述字段。
- 不涉及字段新增的逻辑调整、Bug 修复和赋值修改可以直接进行。

## 文件编辑与代码注释

- 文件编辑优先使用 IDE 的 `replace_string_in_file`、`create_file`、`remove_file`；禁止使用 `mv`、`rm`、`sed` 或 PowerShell 脚本修改文件，除非 IDE 工具无法完成且业务绝对必要。
- 使用当前可用的补丁工具编辑时，保持修改范围最小，不覆盖用户已有改动。
- 方法使用中文三斜杠文档注释；每个 `<param>` 都要说明含义、数据类型、取值范围和是否必填。私有方法同样需要注释。
- 关键判断、复杂算法、条件分支和有副作用的代码前使用中文 `//` 注释。
- 业务 `if`、`else if` 判断前说明具体判断内容。
- 方法内声明的 `bool` 值需要添加中文含义注释。
- 给实体类赋值时，在代码后使用 `//` 标注字段含义。

## 数据库查询

- 编写需要真实数据的文档时，主动使用 MCP 查询数据库。
- 数据库查询仅允许 SELECT、加 TOP/LIMIT，以及敏感字段脱敏或排除。

## 工作方式

- 需要上下游代码上下文时，直接读取关联文件，不需要事先确认；仅在需要用户决策时提问。
- 处理本项目任务时不使用子代理，包括 `run_subagent` 和 `search_agent`。
- 测试文件需要删除；除非用户明确要求，不新增测试文件。
- 每次面向用户的回答都必须以“喵~”结尾。
- 软件开发讨论中的 API 请求/响应数据结构统一称为 DTO、请求 DTO、响应 DTO 或接口数据结构，不使用“合同”一词。
