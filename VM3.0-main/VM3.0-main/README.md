# WPF-VisionMaster
 基于WPF开发的VisionMaster软件

 .gitmodules命令文档：
 https://blog.csdn.net/u010975589/article/details/146072391

# 启动软件
d:\GVision\Source\Apps\G.App.VisionMaster.OpenCV\bin\Debug\net9.0-windows\G.App.VisionMaster.OpenCV.exe

### 一、项目核心概览
1. **基础信息**：基于.NET 9.0-windows框架，解决方案入口为GVision.sln，采用集中式构建配置（Directory.Build.Props/Targets）管理版本、版权、应用图标等全局资源。
2. **目录结构**：核心分为Source（业务/控件）、Solution（解决方案）、Tools（工具）、Setups（安装包）、Document（素材）五大顶层目录。

### 二、核心模块与功能
1. **应用层（OpenCV示例）**
   - 入口：App.xaml（基于自定义ApplicationBase）+ App.xaml.cs（服务注册、启动配置、SplashScreen参数设置）。
   - 主界面：MainWindow.xaml（标题绑定ApplicationProvider.Product）+ MainViewModel.cs（项目上下文管理）。
   - 资源：包含ONNX模型（yolov5s.onnx）等视觉相关素材。
2. **控件与基础库（WPF-Control）**
   - 核心能力：提供MVVM框架、通用控件（Form/PDF/TagBox等）、主题样式、SplashScreen模块（含ISplashScreenViewPresenter接口、SplashScreenViewPresenter.xaml模板）。
3. **视觉领域层（VisionMaster）**
   - 核心：OpenCV适配、视觉节点/数据/网络/项目模型、测试素材（图片/视频/Halcon资源）。
4. **工具与分发**
   - 工具：IconBuilder（生成应用图标）；
   - 分发：Setups目录提供setup.exe安装包。

### 三、启动流程
1. 初始化：App.xaml加载统一配置，App.xaml.cs注册服务、配置SplashScreen（标题/副标题/版权）；
2. 启动界面：渲染SplashScreenViewPresenter.xaml模板，文本来源为SplashScreenOptions与ApplicationProvider；
3. 主窗口：创建MainWindow，标题绑定ApplicationProvider.Product，完成界面初始化。

### 四、关键配置/修改入口
1. 启动界面文案：修改App.xaml.cs中SplashScreenOptions的Product/Sub属性；
2. 主窗口标题：配置程序集AssemblyProduct属性（集中在Directory.Build.Props或主应用csproj）；
3. 版权信息：Directory.Build.Props中配置Copyright字段，通过ApplicationProvider.Copyright读取；
4. 主题/样式：在App.xaml.cs的Configure方法中切换，样式资源集中在H.Style目录。

### 五、操作建议
1. 编译运行：用VS2022+打开解决方案，编译运行H.App.VisionMaster.OpenCV项目；
2. 文案调整：聚焦App.xaml.cs（启动界面）、Directory.Build.Props（版权/产品名）；
3. 业务扩展：参考VisionMaster/NodeDatas下现有视觉节点（如OpenCVSrcImageFilesNodeData）；
4. 打包分发：直接使用Setups目录的setup.exe，或基于现有配置扩展安装包。
