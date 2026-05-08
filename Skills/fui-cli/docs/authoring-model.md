# FUI authoring model

## ViewModel

必须满足：

- `partial class`
- 继承 `FUI.ViewModel`
- 用普通公共属性作为绑定源
- 用 `[ViewBinding]` 标记视图名
- 用 `[Binding]` 标记元素绑定
- 用 `[Command]` 标记命令入口

示例：

```csharp
[ViewBinding("SampleView")]
public partial class SampleViewModel : FUI.ViewModel
{
    [Binding("Title", nameof(TextElement.Text))]
    public string Title { get; set; }

    [Binding("AccountInput", nameof(InputFieldElement.Text), bindingMode: BindingMode.TwoWay)]
    public string Account { get; set; }

    [Command("SubmitButton", nameof(ButtonElement.OnClick))]
    public void OnSubmit()
    {
    }
}
```

## Presenter

Presenter 负责：

- 在 `OnOpen` 中初始化 ViewModel
- 处理导航与业务动作
- 把结果回写到 ViewModel

示例：

```csharp
public class SampleViewPresenter : Presenter<SampleViewModel>
{
    protected override void OnOpen(object param)
    {
        VM.Title = "Hello";
    }
}
```

## 不要这样写

- 不要在 ViewModel 中把绑定字段写成 `BindableProperty<T>`
- 不要手写具体 `BindingContext`
- 不要让 Presenter 直接持有 UI 结构状态，优先通过 ViewModel 表达

## 生成前提

如果 FUI 绑定没有生效，优先检查：

- FUI SourceGenerator 是否已安装到目标 asmdef 可见范围
- IL PostProcess 配置是否存在并指向正确目标程序集
- 目标 ViewModel 是否满足 `partial + ViewBinding + Binding/Command` 基本约束
