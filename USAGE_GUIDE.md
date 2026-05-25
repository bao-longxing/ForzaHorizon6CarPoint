# GotoScriptRace 使用示例

## 快速开始

### 基本调用

```csharp
// 最简单的调用方式
string blueprintCode = "BP_MyBlueprint_001";
ClsGameControl.GotoScriptRace(blueprintCode);
```

### 启用调试模式

```csharp
// 启用调试模式查看中间过程（会显示 OpenCV 窗口）
ClsGameControl.GotoScriptRace(blueprintCode, debug: true);
```

---

## 完整的 UI 按钮实现示例

### XAML 界面代码

```xaml
<!-- 在 MainWindow.xaml 中添加一个按钮来启动脚本赛车 -->
<Border Style="{StaticResource CardBorderStyle}">
	<StackPanel>
		<TextBlock Text="🏁 脚本赛车" 
				   Foreground="{StaticResource TextBrush}" 
				   FontWeight="SemiBold" 
				   Margin="0,0,0,10"/>

		<!-- 蓝图代码输入 -->
		<TextBlock Text="蓝图代码" 
				   Foreground="{StaticResource SubTextBrush}" 
				   VerticalAlignment="Center" 
				   Margin="0,0,0,5"/>
		<TextBox x:Name="txtBlueprintCode" 
				 Height="34" 
				 Background="#0A1322" 
				 Foreground="#DDE8FF" 
				 BorderBrush="#2A3A56" 
				 Text="BP_RACE_001"
				 VerticalContentAlignment="Center" 
				 Margin="0,0,0,10"/>

		<!-- 启动按钮 -->
		<Button x:Name="btnStartScriptRace" 
				Content="▶ 启动脚本赛车" 
				Style="{StaticResource ActionButtonStyle}"
				Click="BtnStartScriptRace_Click"
				Margin="0,0,0,5"/>

		<!-- 当前状态和进度 -->
		<ProgressBar x:Name="prgScriptProgress" 
					 Height="6" 
					 Background="#0A1322" 
					 Foreground="#5B3BE5"
					 Margin="0,10,0,5"
					 Maximum="999"
					 Value="0"/>

		<TextBlock x:Name="txtScriptProgress" 
				   Text="当前点数: 0/999" 
				   Foreground="{StaticResource SubTextBrush}" 
				   Margin="0,0,0,5"/>

		<TextBlock x:Name="txtScriptStatus" 
				   Text="状态: 就绪" 
				   Foreground="{StaticResource SubTextBrush}"/>
	</StackPanel>
</Border>
```

### C# 代码后台

```csharp
// 在 MainWindow.xaml.cs 中添加以下代码

private bool _isScriptRunning = false;

/// <summary>
/// 启动脚本赛车按钮事件
/// </summary>
private void BtnStartScriptRace_Click(object sender, RoutedEventArgs e)
{
	if (_isScriptRunning)
	{
		AppendLog("[警告] 脚本已在运行，请勿重复启动");
		return;
	}

	string blueprintCode = txtBlueprintCode.Text?.Trim();
	if (string.IsNullOrEmpty(blueprintCode))
	{
		AppendLog("[错误] 请输入蓝图代码");
		return;
	}

	AppendLog($"[开始] 启动脚本赛车，蓝图代码: {blueprintCode}");
	_isScriptRunning = true;
	btnStartScriptRace.IsEnabled = false;

	// 在后台线程执行脚本
	Task.Run(() =>
	{
		try
		{
			ClsGameControl.GotoScriptRace(blueprintCode, debug: false);
		}
		catch (OperationCanceledException)
		{
			AppendLog("[信息] 脚本被用户取消");
		}
		catch (Exception ex)
		{
			AppendLog($"[错误] 脚本执行失败: {ex.Message}");
		}
		finally
		{
			_isScriptRunning = false;
			Dispatcher.Invoke(() =>
			{
				btnStartScriptRace.IsEnabled = true;
				txtScriptStatus.Text = "状态: 已停止";
			});
		}
	});
}

/// <summary>
/// 事件处理：蓝图执行开始
/// </summary>
private void OnBlueprintExecutionStarted(object? sender, EventArgs e)
{
	try
	{
		Dispatcher.Invoke(() =>
		{
			AppendScriptLog("[事件] 蓝图执行开始");
			txtScriptStatus.Text = "状态: 执行中";
			prgScriptProgress.Value = 0;
			txtScriptProgress.Text = "当前点数: 0/999";
		});
	}
	catch (Exception ex)
	{
		AppendLog($"[错误] OnBlueprintExecutionStarted: {ex.Message}");
	}
}

/// <summary>
/// 事件处理：点数进度变更
/// </summary>
private void OnPointProgressChanged(object? sender, ClsGameControl.PointProgressChangedEventArgs e)
{
	try
	{
		if (e != null)
		{
			Dispatcher.Invoke(() =>
			{
				AppendPointLog($"[进度] 当前点数: {e.CurrentPoints}/{e.TotalPoints}");
				prgScriptProgress.Value = e.CurrentPoints;
				txtScriptProgress.Text = $"当前点数: {e.CurrentPoints}/{e.TotalPoints}";

				// 计算完成百分比
				double percentage = (e.CurrentPoints / (double)e.TotalPoints) * 100;
				txtScriptProgress.Text = $"当前点数: {e.CurrentPoints}/{e.TotalPoints} ({percentage:F1}%)";
			});
		}
	}
	catch (Exception ex)
	{
		AppendLog($"[错误] OnPointProgressChanged: {ex.Message}");
	}
}

/// <summary>
/// 事件处理：点数完成
/// </summary>
private void OnPointCompletionCompleted(object? sender, EventArgs e)
{
	try
	{
		Dispatcher.Invoke(() =>
		{
			AppendPointLog("[完成] 🎉 点数已达到999，任务完成！");
			AppendScriptLog("[事件] 蓝图执行完成 - 点数已达到999");
			txtScriptStatus.Text = "状态: 已完成 ✓";
			prgScriptProgress.Value = 999;
			txtScriptProgress.Text = "当前点数: 999/999 (100%)";
		});
	}
	catch (Exception ex)
	{
		AppendLog($"[错误] OnPointCompletionCompleted: {ex.Message}");
	}
}
```

---

## 蓝图代码示例

### 有效的蓝图代码格式

```
BP_RACE_001          # 简单名称
MyBlueprint_v1.0     # 带版本号
RACE-MOD-2024        # 带横线分隔
Race_Code_2024       # 混合格式
RaceBlueprint123     # 字母+数字
```

### 输入支持

```csharp
// InputText 支持以下字符类型：

// 1. 大小写字母
ClsLogicContorl_Ghub.InputText("AbCdEfG");

// 2. 数字
ClsLogicContorl_Ghub.InputText("0123456789");

// 3. 常见符号
ClsLogicContorl_Ghub.InputText("BP_RACE-001");       // 下划线、横线
ClsLogicContorl_Ghub.InputText("file.txt");          // 点号
ClsLogicContorl_Ghub.InputText("hello@world");       // @符号
ClsLogicContorl_Ghub.InputText("test(1)");           // 括号
ClsLogicContorl_Ghub.InputText("100%");              // %符号
ClsLogicContorl_Ghub.InputText("$$$");               // $符号

// 4. 空格
ClsLogicContorl_Ghub.InputText("My Blueprint Code");
```

---

## 事件订阅完整示例

```csharp
// 在窗口加载时订阅事件
public MainWindow()
{
	InitializeComponent();

	// 订阅所有事件
	ClsGameControl.BlueprintExecutionStarted += OnBlueprintExecutionStarted;
	ClsGameControl.PointProgressChanged += OnPointProgressChanged;
	ClsGameControl.PointCompletionCompleted += OnPointCompletionCompleted;

	AppendLog("[信息] 所有事件已订阅");
}

// 在窗口关闭时注销事件
private void Window_Closing(object sender, CancelEventArgs e)
{
	// 注销事件
	ClsGameControl.BlueprintExecutionStarted -= OnBlueprintExecutionStarted;
	ClsGameControl.PointProgressChanged -= OnPointProgressChanged;
	ClsGameControl.PointCompletionCompleted -= OnPointCompletionCompleted;
}
```

---

## 高级用法

### 1. 条件启动

```csharp
private void BtnStartScriptRace_Click(object sender, RoutedEventArgs e)
{
	// 检查前置条件
	if (!ClsObs.IsConnected)
	{
		AppendLog("[错误] OBS 未连接，无法启动脚本");
		return;
	}

	if (!ClsROI.TargetRects.ContainsKey(ClsROI.UIElem.大世界安娜))
	{
		AppendLog("[错误] ROI 未配置，请先设置区域");
		return;
	}

	// 启动脚本
	Task.Run(() => ClsGameControl.GotoScriptRace(blueprintCode));
}
```

### 2. 进度保存

```csharp
private void OnPointProgressChanged(object? sender, ClsGameControl.PointProgressChangedEventArgs e)
{
	if (e != null && e.CurrentPoints % 99 == 0)  // 每 99 点保存一次
	{
		// 保存进度到数据库或文件
		SaveProgress(e.CurrentPoints);
		AppendLog($"[保存] 进度已保存: {e.CurrentPoints} 点");
	}
}

private void SaveProgress(int points)
{
	// 实现进度保存逻辑
	try
	{
		string progressFile = "progress.txt";
		File.WriteAllText(progressFile, points.ToString());
	}
	catch (Exception ex)
	{
		AppendLog($"[错误] 保存进度失败: {ex.Message}");
	}
}
```

### 3. 调试模式

```csharp
// 启用调试窗口查看中间过程
private void BtnStartScriptRaceDebug_Click(object sender, RoutedEventArgs e)
{
	string blueprintCode = txtBlueprintCode.Text?.Trim();

	Task.Run(() =>
	{
		AppendLog("[调试] 启动调试模式");
		ClsGameControl.GotoScriptRace(blueprintCode, debug: true);
		AppendLog("[调试] 调试模式完成，请查看 OpenCV 窗口");
	});
}
```

### 4. 超时处理

```csharp
private void BtnStartScriptRaceWithTimeout_Click(object sender, RoutedEventArgs e)
{
	string blueprintCode = txtBlueprintCode.Text?.Trim();

	// 设置总超时时间（例如 30 分钟）
	var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));

	Task.Run(async () =>
	{
		try
		{
			ClsGameControl.GotoScriptRace(blueprintCode);
		}
		catch (OperationCanceledException)
		{
			AppendLog("[超时] 脚本执行超时，已停止");
		}
	}, cts.Token);
}
```

---

## 常见问题

### Q1: 如何中途停止脚本？
**A**: 按下 F12 键即可取消脚本执行。F12 取消机制在每个关键步骤都会检查。

### Q2: 蓝图代码输入失败怎么办？
**A**: 
1. 检查 ClsLogicContorl_Ghub 是否初始化成功
2. 确保蓝图代码字符都在支持的范围内
3. 增加 InputText 中字符间隔时间

### Q3: 如何验证 ROI 配置是否正确？
**A**: 
1. 使用调试模式（`debug: true`）
2. OpenCV 窗口会显示裁剪后的区域
3. 确认显示的区域与预期一致

### Q4: 点数没有更新怎么办？
**A**: 
1. 检查"重新开始"按钮是否被正确识别
2. 增加检测间隔时间（`restartDetectIntervalMs`）
3. 查看日志中是否有 OCR 识别失败的记录

### Q5: 脚本超时了怎么办？
**A**: 
- 默认 2 分钟超时（可在代码中修改 `restartDetectTimeoutMs`）
- 可能原因：游戏响应慢、网络延迟、ROI 配置错误
- 解决方法：增加超时时间或检查游戏状态

---

## 性能提示

1. **禁用调试模式** - 调试模式会显示额外的 OpenCV 窗口，会影响性能
2. **优化 OCR** - 裁剪的 ROI 区域越小，OCR 识别越快
3. **调整检测间隔** - 如果网络稳定，可以减少检测间隔以加快速度
4. **后台运行** - 脚本应在后台任务中运行，避免阻塞 UI

---

## 故障排除

| 问题 | 原因 | 解决方案 |
|------|------|--------|
| "未识别到'安娜'" | ROI 配置错误或区域不清晰 | 重新配置 ROI，启用调试模式检查 |
| "未能识别到有效的技术点数" | 点数文本识别失败 | 调整 ROI 或检查 OCR 模型 |
| "2分钟内未检测到重新开始按钮" | 赛车任务未启动或按钮位置变化 | 检查游戏状态，重新配置"重新开始"ROI |
| 蓝图代码输入错误 | 字符不支持或输入速度过快 | 使用支持的字符，增加间隔时间 |
| F12 取消无响应 | KeyboardHook 未初始化 | 检查 KeyboardHook 初始化是否成功 |

---

## 性能统计

典型执行时间（点数 0 → 999）：
- 初始化阶段：约 30 秒
- 每个赛车循环：约 50-60 秒
- 总耗时：约 80-90 分钟（18-20 个循环）

这取决于：
- 游戏响应速度
- OBS 连接稳定性
- 网络延迟
- CPU/GPU 性能

---

## 许可和致谢

- 项目使用开源库：OpenCvSharp, PaddleOCR, OBS WebSocket API
- 遵循相应库的许可协议

---

**版本**: 1.0  
**最后更新**: 2024年  
**状态**: 文档完成 ✅
