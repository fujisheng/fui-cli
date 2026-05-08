# 运行时接入思路

## 最小接入目标

一个可运行的 FUI 工程，至少需要：

- 能按 `viewName` 找到 prefab
- 能创建 `IViewFactory`
- 能初始化 `UIManager`
- 能在运行时打开指定视图

## 典型结构

运行时一般分成三层：

1. **AssetLoader / AssetLoaderFactory**
   - 根据 `viewName` 返回 prefab 或实例
2. **ViewFactory**
   - 基于 loader 创建 `IView`
3. **UIManager**
   - 打开、关闭、切换视图

## 最小示例

```csharp
var viewFactory = new ViewFactory(assetLoaderFactory);
var uiManager = new UIManager(viewFactory);
uiManager.Initialize();
uiManager.Open("SampleView");
```

## 共享状态

如果多个页面共享同一份数据：

- 用独立 ViewModel 承载共享状态
- 通过 `DynamicViewElement.Source` / `DynamicViewElement.Data` 传入
- 避免在多个页面里复制同一份业务状态

## 运行时自动化前提

在用 FUI CLI 做 PlayMode 验证前，优先确认：

- 场景里存在 `EventSystem`
- UI 已初始化完成
- 要验证的视图可以被 `UIManager` 打开
- Editor 不在 compiling / updating / modal 状态

## 不要绑定到具体项目结构

这个技能不假设：

- 固定 prefab 目录
- 固定资源加载方式
- 固定启动入口类名
- 固定业务页面流转

只要你的工程满足 `viewName -> prefab -> IViewFactory -> UIManager` 这条链，就能接入 FUI CLI。
