# GotoScriptRace 方法实现总结

## 📋 项目概述
本实现为 Forza Horizon 6 脚本工具添加了完整的**脚本赛车**功能，包括事件系统、完整的游戏控制流程和用户界面集成。

---

## 🎯 核心功能

### 1. **事件系统** (ClsGameControl.cs)
三个公共事件供 UI 层订阅：

#### 📡 `BlueprintExecutionStarted` (EventHandler)
- **触发时机**: 蓝图执行开始
- **用途**: 用于启动定时器或其他初始化操作
- **订阅方**: MainWindow.xaml.cs

#### 📊 `PointProgressChanged` (EventHandler<PointProgressChangedEventArgs>)
- **参数**: `PointProgressChangedEventArgs`
  - `int CurrentPoints` - 当前积分
  - `int TotalPoints` - 总积分（固定为 999）
- **触发时机**: 每次完成一个赛车循环后（点数 < 999）
- **用途**: 更新进度条和显示当前点数
- **订阅方**: MainWindow.xaml.cs

#### 🏁 `PointCompletionCompleted` (EventHandler)
- **触发时机**: 点数达到或超过 999 时
- **用途**: 显示完成消息或触发后续操作
- **订阅方**: MainWindow.xaml.cs

---

## 🔧 GotoScriptRace 方法详解

### 方法签名
```csharp
public static void GotoScriptRace(string blueprintCode, bool debug = false)
```

### 参数说明
- **blueprintCode** (string): 蓝图代码，将通过键盘输入到游戏中
- **debug** (bool): 是否显示调试窗口（OpenCV 窗口），默认 false

### 执行流程（12 个步骤）

#### ✅ 步骤 0: 初始化
- 检查 F12 取消令牌
- 触发 `BlueprintExecutionStarted` 事件
- 记录日志

#### ✅ 步骤 1-2: 识别大世界安娜区域
1. 获取 OBS 截图
2. 从 ClsROI 中读取"大世界安娜"ROI 坐标
3. OCR 识别区域内是否包含"安娜"字样
4. 如果不包含，返回并记录日志
5. 如果包含，继续执行

**键盘操作**:
- 按下 ESC，等待 100ms
- 按下 PageDown，等待 300ms

#### ✅ 步骤 3: 识别车辆界面技术点数
1. 获取 OBS 截图
2. 从 ClsROI 中读取"车辆界面技术点数"ROI 坐标
3. OCR 识别文本并移除"技术点"、"可用"等修饰词
4. 提取数字并转换为 int 类型
5. 保存为 `techPoints` 变量（步骤 10 会用到）

#### ✅ 步骤 4: 导航操作
- 按下 PageDown 3 次（间隔 200ms）
- 按下 Enter/Enter/BackSpace/Up/Enter（间隔 100ms）

#### ✅ 步骤 5: 输入蓝图代码
- 使用 `ClsLogicContorl_Ghub.InputText()` 输入蓝图代码
- 支持大小写字母、数字、常见符号
- 间隔 10ms 以确保可靠输入

#### ✅ 步骤 6: 确认蓝图输入
- 按下 Enter/Down/Enter（间隔 100ms）
- 等待 5 秒

#### ✅ 步骤 7: 启动赛车任务
- 按下 Enter（等 2 秒）
- 按下 Enter（等 2 秒）
- 按下 Enter

#### ✅ 步骤 8: 等待任务启动
- 等待 15 秒
- 按下 Enter

#### ✅ 步骤 9-12: 循环执行赛车

**步骤 9: 按 W 并检测重新开始按钮**
- 按下 W（按住）
- 循环检测 ClsROI 中的"重新开始"按钮（每 500ms 检测一次）
- 最多等待 2 分钟（120000ms）
- 如果检测到"重新开始"按钮，继续到步骤 10

**步骤 10-11: 判断是否完成**
```
if (currentPoints >= 999)
	// 步骤 11: 完成
else
	// 步骤 12: 继续循环
```

**步骤 11: 点数达到 999**
- 弹起 W
- 按下 Enter
- 等待 20 秒
- 触发 `PointCompletionCompleted` 事件
- 任务完成

**步骤 12: 点数 < 999**
- 弹起 W
- 将 `currentPoints += 9`
- 触发 `PointProgressChanged` 事件（发送当前点数）
- 按下 X，等待 400ms
- 按下 Enter，等待 100ms
- 等待 8 秒
- 按下 Enter
- 回到步骤 9（继续循环）

---

## 📝 文件修改详情

### 1. ClsGameControl.cs
**新增内容**:
- 事件定义和相关的 raise 方法（40 行代码）
- `GotoScriptRace` 方法（完整实现，约 360 行代码）

**关键方法**:
```csharp
// 事件定义
public static event EventHandler? BlueprintExecutionStarted;
public static event EventHandler<PointProgressChangedEventArgs>? PointProgressChanged;
public static event EventHandler? PointCompletionCompleted;

// 事件参数类
public class PointProgressChangedEventArgs : EventArgs
{
	public int CurrentPoints { get; set; }
	public int TotalPoints => 999;
}

// 主方法
public static void GotoScriptRace(string blueprintCode, bool debug = false)
```

### 2. ClsLogicContorl_Ghub.cs
**新增内容**:
- 改进的 `InputText` 方法（使用 KeyMap 和 Shift 组合）

**功能**:
- 支持大小写字母
- 支持数字
- 支持常见符号（需要 Shift 的符号通过组合键实现）
- 使用 KeyMap 进行键映射，确保兼容性

### 3. MainWindow.xaml.cs
**新增内容**:
- 三个事件订阅（在构造函数中）
- 三个事件处理方法
- 事件注销（在 Window_Closing 中）

**事件处理方法**:
```csharp
private void OnBlueprintExecutionStarted(object? sender, EventArgs e)
private void OnPointProgressChanged(object? sender, ClsGameControl.PointProgressChangedEventArgs e)
private void OnPointCompletionCompleted(object? sender, EventArgs e)
```

---

## 🔄 集成点

### 与现有系统的集成

1. **F12 取消机制**
   - 每个关键步骤都检查 `CancellationToken`
   - 按 F12 可随时取消操作

2. **OCR 系统**
   - 使用 `ClsOCR.RecognizeFromBytes()` 进行文本识别
   - 支持中文和英文识别

3. **ROI 管理**
   - 从 `ClsROI.TargetRects` 中读取已定义的区域
   - 支持坐标缩放和映射

4. **键盘控制**
   - 使用 `ClsLogicContorl_Ghub` 进行所有键盘输入
   - 使用 KeyMap 确保键映射的准确性

5. **日志系统**
   - 使用 `ClsLogger.LogScript()` 记录脚本执行日志
   - 所有步骤都有相应的日志记录

6. **截图系统**
   - 使用 `ClsObs` 获取游戏截图
   - 支持 PNG 编码和 base64 转换

---

## 🧪 测试建议

### 测试步骤
1. 启动应用并初始化所有系统
2. 进入游戏并导航到大世界安娜区域
3. 在界面上添加"启动脚本赛车"按钮（需要蓝图代码输入）
4. 点击按钮启动 `GotoScriptRace` 方法
5. 观察以下内容：
   - 日志输出是否正确
   - 事件是否按时触发
   - 点数是否正确更新
   - F12 是否能正确取消

### 调试模式
```csharp
// 启用调试窗口查看中间过程
ClsGameControl.GotoScriptRace(blueprintCode, debug: true);
```

---

## 📊 事件流示例

```
蓝图执行开始
	↓
[识别安娜] → [启动游戏] → [识别点数] → [输入蓝图代码]
	↓
第一个赛车循环
	↓
触发 PointProgressChanged(18/999)
	↓
第二个赛车循环
	↓
触发 PointProgressChanged(27/999)
	↓
...继续循环...
	↓
当点数 >= 999 时
	↓
触发 PointCompletionCompleted
	↓
任务完成
```

---

## ⚙️ 配置项

### 常数定义
```csharp
const int keyHoldMs = 80;              // 按键保持时间（ms）
const int restartDetectTimeoutMs = 120000;  // 2分钟超时
const int restartDetectIntervalMs = 500;    // 检测间隔（ms）
```

这些值可根据需要调整。

---

## 🐛 错误处理

所有步骤都包含：
- ✅ F12 取消检查
- ✅ 异常捕获和记录
- ✅ 返回值验证
- ✅ 日志记录

---

## 📌 关键注意事项

1. **蓝图代码格式**: 输入的蓝图代码应为字符串格式，支持大小写字母、数字和常见符号

2. **ROI 配置**: 确保以下 ROI 已在 targetRects.json 中配置：
   - `大世界安娜`
   - `车辆界面技术点数`
   - `重新开始`

3. **OBS 连接**: 方法依赖于 OBS 连接，确保 OBS 已正确初始化

4. **游戏窗口**: 确保游戏窗口处于活跃状态并正确聚焦

5. **超时处理**: 2 分钟内未检测到"重新开始"按钮时自动退出循环

---

## ✨ 后续优化建议

1. **UI 增强**
   - 添加进度条显示当前点数 / 999
   - 显示实时事件通知
   - 添加"启动脚本赛车"按钮

2. **功能扩展**
   - 支持多个蓝图代码循环
   - 保存进度（在某个断点暂停后继续）
   - 添加统计数据（总耗时、完成次数等）

3. **性能优化**
   - 缓存 OCR 模型以加快识别速度
   - 优化截图和识别的时间间隔

4. **错误恢复**
   - 添加自动重试机制
   - 在检测失败时的回退策略

---

## 📞 支持信息

- **语言**: C# (.NET 10)
- **依赖**: OpenCvSharp, PaddleOCR, OBS WebSocket API
- **编译状态**: ✅ 成功
- **运行环境**: Windows（通过 Forza Horizon 6）

---

**实现完成日期**: 2024年
**版本**: 1.0
**状态**: 生产就绪 ✅
