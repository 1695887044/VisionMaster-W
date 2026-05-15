using System.Runtime.CompilerServices;

namespace HslCommunication
{
	/// <summary>
	/// 一个工业物联网的底层架构框架，专注于底层的技术通信及跨平台，跨语言通信功能，实现各种主流的PLC数据读写，实现modbus，机器人的各种协议读写等等，
	/// 支持快速搭建工业上位机软件，组态软件，SCADA软件，工厂MES系统，助力企业工业4.0腾飞，实现智能制造，智慧工厂的目标。
	/// <br /><br />
	/// 本组件付费开源，使用之前请认真的阅读本API文档，对于本文档中警告部分的内容务必理解，部署生产之前请详细测试，如果在测试的过程中，
	/// 发现了BUG，或是有问题的地方，欢迎联系作者进行修改，或是直接在github上进行提问。未经测试，直接部署，对设备，工厂造成了损失，作者概不负责。
	/// <br /><br />
	/// 官方网站：<a href="http://www.hslcommunication.cn/">http://www.hslcommunication.cn/</a>，包含组件的在线API地址以及一个MES DEMO的项目展示。
	/// <br /><br />
	/// <note type="important">
	/// 本组件的目标是集成一个框架，统一所有的设备读写方法，抽象成统一的接口<see cref="T:HslCommunication.Core.IReadWriteNet" />，对于上层操作只需要关注地址，读取类型即可，另一个目标是使用本框架轻松实现C#后台+C#客户端+web浏览器+android手机的全方位功能实现。
	/// </note>
	/// 本库提供了C#版本和java版本和python版本，java，python版本的使用和C#几乎是一模一样的，都是可以相互通讯的。
	/// <br />
	/// 在使用本通讯库之前，需要学会如何使用nuget来安装当前的通讯库，可以参考如下的博文：<a href="http://www.cnblogs.com/dathlin/p/7705014.html">http://www.cnblogs.com/dathlin/p/7705014.html</a>
	/// <br /><br />
	/// 先整体介绍下如何使用本组件库的基本思路，基本上是引用库，从nuget安装的库会自动添加引用到项目中的，就可以直接进行using操作了，当然了在使用之前，需要先激活一下，激活的方式如下：
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Active.cs" region="Sample1" title="激活示例" />
	/// 在你的应用程序刚开起来的时候，激活一次即可，后续都不需要再重复激活了。接下来就可以开始写代码了。任何的设备的操作基本是相同的，实例化，配置参数（有些plc默认的参数即可），连接设备，读写操作，关闭
	/// <br />
	/// 关于Hsl的日志功能，贯穿整个hslcommunication的项目，所有的网络类，都包含了<see cref="T:HslCommunication.LogNet.ILogNet" />日志功能，当然你也可以继承接口实现你自己的日志，在hsl里提供了三种常见的简单实用的日志
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\LogNet\LogNetSingle.cs" region="Example1" title="单文件实例化" />
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\LogNet\LogNetSingle.cs" region="Example4" title="日志基本的使用" />
	/// <br />
	/// 再开始讲解基本的代码通讯之前，先来了解两个基本的概念，长连接，短连接。为了更好的说明当前的通信情况，我把所有的通信拆分为四个部分，连接，发，收，断开。<br />
	/// 短连接：连接，发，收，断开，连接，发，收，断开，连接，发，收，断开，连接，发，收，断开...无限循环<br />
	/// 长连接：连接，发，收，发，收，发，收，发，收，发，收，发，收，发，收，发，收，发，收....断开<br />
	/// 然后我们来看看异常的情况，短连接的异常比较好处理，反正每次请求都是先连接，关键来看长连接的异常<br />
	/// 长连接：连接，发，收，发，收...异常，连接，发，收，发，收，异常，连接，连接，连接...收，发，收，发，收，发，收，发，收....断开<br />
	/// 这里第一个异常发生后，第二次读写立即连接上去并且成功，第二个异常触发后，一直读写失败，说明就是一直连接不上去。<br />
	/// 对于HSL组件来说，不需要重复连接服务器或是plc，无论是短连接还是长连接，都只需要一直读写就OK了，对读写的结果进行判定，即使发生异常了，读写失败了，也要一直坚持，网络好的时候，读写会恢复成功的。<br /><br />
	/// 我们以三菱的PLC为示例，其他的plc调用方式基本是一模一样的，就是调用的类不一样，参数配置不一样而已。以下的逻辑都是适用的。
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\melsecTest.cs" region="Usage" title="简单的短连接使用" />
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\melsecTest.cs" region="Usage2" title="简单的长连接使用" />
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\melsecTest.cs" region="ReadExample1" title="基本的读取示例" />
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\melsecTest.cs" region="ReadExample2" title="批量读取示例" />
	/// 需要注意的事，在实际的开发中，我们的一个窗体程序（或是控制台，原理都是一样的），会定时或是不定时的去读写PLC的操作（调用Read或是Write方法），这个本身是没有任何问题的，
	/// 但是总会有这样的需求，我们需要在界面上，或是系统里实时体现当前的PLC的在线情况，我相信不少小伙伴会有这样的问题的。所以就出现了下面的代码：
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\melsecTest.cs" region="Check Netstatus" title="检查连接状态" />
	/// 实际这种操作是非常不可取的，为什么这么说，下面说说原因：<br />
	/// <note type="warning">
	/// 首先说明下<see cref="M:HslCommunication.Core.Net.NetworkDoubleBase.ConnectServer" />方法里发生了什么？这个方法首先会关闭连接，然后重新连接，连接成功，就发送初始化指令（有些PLC就需要握手确认），初始化握手成功，才返回真正的成功！<br />
	/// 那么这里为什么不行呢？因为Read和Write方法是有混合锁实现互斥操作的，这样的好处就是多线程调用互不影响，但是<see cref="M:HslCommunication.Core.Net.NetworkDoubleBase.ConnectServer" />方法，并没有互斥锁，如果调用的时候同时在读写，那就会导致异常，
	/// 那么为什么没有加互斥锁呢？因为为了实现读写方法的时候，支持自动重连操作，所以连接方法已经在互斥锁了。如果再加互斥锁，会发生死锁，所以综合考虑，就设计成了现在的样子。
	/// </note>
	/// 既然上面的代码不能使用，那么怎么来看当前的连接状态呢？这里有一点需要注意，<see cref="M:HslCommunication.Core.Net.NetworkDoubleBase.ConnectServer" />只需要调用0次或1次即可。
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Profinet\melsecTest.cs" region="Check Netstatus2" title="检查连接状态" />
	/// 如果你本来就在每秒读取PLC的数据信息了，那么连检测的定时器都不用写了，你每次读取数据的时候顺便判定下，结果就出来了。<br /><br />
	/// 其他相关的代码示例需要到各自的目录里查找，下面只列举了一些常见的代码示例
	/// <list type="bullet">
	///     <item>Hsl组件日志相关示例参考<see cref="T:HslCommunication.LogNet.ILogNet" /></item>
	///     <item>三菱mc协议示例参考<see cref="T:HslCommunication.Profinet.Melsec.MelsecMcNet" /></item>
	///     <item>西门子S7协议示例参考<see cref="T:HslCommunication.Profinet.Siemens.SiemensS7Net" /></item>
	///     <item>欧姆龙协议示例参考<see cref="T:HslCommunication.Profinet.Omron.OmronFinsNet" /></item>
	///     <item>罗克韦尔协议示例参考<see cref="T:HslCommunication.Profinet.AllenBradley.AllenBradleyNet" /></item>
	///     <item>Modbus协议示例参考<see cref="T:HslCommunication.ModBus.ModbusTcpNet" /></item>
	///     <item>MQTT服务器示例参考<see cref="T:HslCommunication.MQTT.MqttServer" /></item>
	///     <item>MQTT客户端示例参考<see cref="T:HslCommunication.MQTT.MqttClient" /></item>
	///     <item>WebSocket服务器示例参考<see cref="T:HslCommunication.WebSocket.WebSocketServer" /></item>
	///     <item>WebSocket客户端示例参考<see cref="T:HslCommunication.WebSocket.WebSocketClient" />。</item>
	///     <item>WebApi示例参考<see cref="T:HslCommunication.Enthernet.HttpServer" />。</item>
	///     <item>Redis示例参考<see cref="T:HslCommunication.Enthernet.Redis.RedisClient" /></item>
	///     <item>身份证阅读器串口版<see cref="T:HslCommunication.Profinet.IDCard.SAMSerial" /></item>
	///     <item>身份证阅读器网口版<see cref="T:HslCommunication.Profinet.IDCard.SAMTcpNet" /></item>
	///     <item>文件传送服务器<see cref="T:HslCommunication.Enthernet.UltimateFileServer" /></item>
	/// </list>
	/// <note type="important">
	/// 相关的代码示例，可以翻阅左侧的命名空间，基本是按照功能来区分的，只要点进去多看看即可
	/// </note>
	/// </summary>
	/// <remarks>
	/// 本软件著作权归Richard.Hu所有。
	/// <br />
	/// 博客地址：<a href="https://www.cnblogs.com/dathlin/p/7703805.html">https://www.cnblogs.com/dathlin/p/7703805.html</a>
	/// <br />
	/// 授权付费模式：Hslcommunication企业商业授权，一次付费，终身授权，终身开放源码，终身支持后续更新，支持的设备列表请参考最新的DEMO界面，凡是DEMO列举都是统一打包授权的。不限制电脑，项目，plc，开发人员数量。
	/// <br />
	/// <list type="bullet">
	///     <item>企业授权提供专业版通讯库的所有更新版的 HslCommunication 源代码。包含 .Net Java Python 三大平台。</item>
	///     <item>企业授权支持对特殊需求而进行修改，更新源代码的服务，配合企业客户修复源代码错误的服务。</item>
	///     <item>企业授权用户拥有对通讯库商用的权利，拥有自己修改源代码并商业使用的权利，组件版权仍归属原作者。</item>
	///     <item>企业授权用户需要对源代码保密。禁止公开源代码，禁止对源代码的商业用途。</item>
	///     <item>企业授权用户可以免费获得官网的 MES DEMO源代码。</item>
	///     <item>企业商业授权 费用请联系QQ200962190咨询，公司即可拥有商用版权，支持任意的开发人员数量，项目数量，支持源代码更新，长期支持，商用软件必须冠名公司标识，官网显示合作伙伴logo。</item>
	///     <item>支持专业的一对一培训业务，一小时1000 rmb，一天8小时为5000 rmb</item>
	/// </list>
	///
	/// 个人赞助二维码：<br />
	/// <img src="https://raw.githubusercontent.com/dathlin/HslCommunication/master/imgs/support.png" />
	/// </remarks>
	/// <revisionHistory>
	///     <revision date="2017-10-21" version="3.7.10" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>正式发布库到互联网上去。</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-10-21" version="3.7.11" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>添加xml文档</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-10-31" version="3.7.12" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>重新设计西门子的数据读取机制，提供一个更改类型的方法。</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-11-06" version="3.7.13" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>提供一个ModBus的服务端引擎。</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-11-07" version="3.7.14" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>紧急修复了西门子批量访问时出现的BUG。</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-11-12" version="3.7.15" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>完善CRC16校验码功能，完善数据库辅助类方法。</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-11-13" version="3.7.16" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>西门子访问类，提供一个批量bool数据写入，但该写入存在安全隐患，具体见博客。</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-11-21" version="4.0.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>与3.X版本不兼容，谨慎升级。如果要升级，主要涉及的代码包含PLC的数据访问和同步数据通信。</item>
	///             <item>删除了2个类，OperateResultBytes和OperateResultString类，提供了更加强大方便的泛型继承类，多达10个泛型参数。地址见http://www.cnblogs.com/dathlin/p/7865682.html</item>
	///             <item>将部分类从HslCommunication命名空间下移动到HslCommunication.Core下面。</item>
	///             <item>提供了一个通用的ModBus TCP的客户端类，方便和服务器交互。</item>
	///             <item>完善了HslCommunication.BasicFramework.SoftBaisc下面的辅助用的静态方法，提供了一些方便的数据转化，在上面进行公开。</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-11-24" version="4.0.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>更新了三菱的读取接口，提供了一个额外的字符串表示的方式，OperateResult&lt;byte[]&gt; read =  melsecNet.ReadFromPLC("M100", 5);</item>
	///             <item>更新了西门子的数据访问类和modbus tcp类提供双模式运行，按照之前版本的写法是默认模式，每次请求重新创建网络连接，新增模式二，在代码里先进行连接服务器方法，自动切换到模式二，每次请求都共用一个网络连接，内部已经同步处理，加速数据访问，如果访问失败，自动在下次请求是重新连接，如果调用关闭连接服务器，自动切换到模式一。</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-11-25" version="4.0.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复Modbus tcp批量写入寄存器时，数据解析异常的BUG。</item>
	///             <item>三菱访问器新增长连接模式。</item>
	///             <item>三菱访问器支持单个M写入，在数组中指定一个就行。</item>
	///             <item>三菱访问器提供了float[]数组写入的API。</item>
	///             <item>三菱访问器支持F报警器，B链接继电器，S步进继电器，V边沿继电器，R文件寄存器读写，不过还需要大面积测试。</item>
	///             <item>三菱访问器的读写地址支持字符串形式传入。</item>
	///             <item>其他的细节优化。</item>
	///             <item>感谢 hwdq0012 网友的测试和建议。</item>
	///             <item>感谢 吃饱睡好 好朋友的测试</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-11-27" version="4.0.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>三菱，西门子，Modbus tcp客户端内核优化重构。</item>
	///             <item>三菱，西门子，Modbus tcp客户端提供统一的报文测试方法，该方法也是通信核心，所有API都是基于此扩展起来的。</item>
	///             <item>三菱，西门子，Modbus tcp客户端提供了一些便捷的读写API，详细参见对应博客。</item>
	///             <item>三菱的地址区分十进制和十六进制。</item>
	///             <item>优化三菱的位读写操作。</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-11-28" version="4.1.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复西门子读取的地址偏大会出现异常的BUG。</item>
	///             <item>完善统一了所有三菱，西门子，modbus客户端类的读写方法，已经更新到博客。</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-12-02" version="4.1.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>完善日志记录，提供关键字记录操作。</item>
	///             <item>三菱，西门子，modbus tcp客户端提供自定义数据读写。</item>
	///             <item>modbus tcp服务端提供数据池功能，并支持数据订阅操作。</item>
	///             <item>提供一个纵向的进度控件。</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-12-04" version="4.1.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>完善Modbus tcp服务器端的数据订阅功能。</item>
	///             <item>进度条控件支持水平方向和垂直方向两个模式。</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-12-05" version="4.1.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>进度条控件修复初始颜色为空的BUG。</item>
	///             <item>进度条控件文本锯齿修复。</item>
	///             <item>按钮控件无法使用灰色按钮精灵破解。</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-12-13" version="4.1.4" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>modbus tcp提供读取short数组的和ushort数组方法。</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-12-13" version="4.1.5" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复流水号生成器无法生成不带日期格式的流水号BUG。</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-12-18" version="4.1.6" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>OperateResult成功时，消息为成功。</item>
	///             <item>数据库辅助类API添加，方便的读取聚合函数。</item>
	///             <item>日志类分析工具界面，显示文本微调。</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-12-25" version="4.1.7" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>进度条控件新增一个新的属性对象，是否使用动画。</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-12-27" version="4.1.8" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>新增一个饼图控件。</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-12-28" version="4.1.9" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>饼图显示优化，新增是否显示百分比的选择。</item>
	///         </list>
	///     </revision>
	///     <revision date="2017-12-31" version="4.2.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>新增一个仪表盘控件。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-01-03" version="4.2.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>饼图控件新增一个是否显示占比很小的信息文本。</item>
	///             <item>新增一个旋转开关控件。</item>
	///             <item>新增一个信号灯控件。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-01-05" version="4.2.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复modbus tcp客户端读取 float, int, long,的BUG。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-01-08" version="4.2.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复modbus tcp客户端读取某些特殊设备会读取不到数据的BUG。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-01-15" version="4.2.4" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>双模式的网络基类中新增一个读取超时的时间设置，如果为负数，那么就不验证返回。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-01-24" version="4.3.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>信号灯控件显示优化。</item>
	///             <item>Modbus Tcp服务端类修复内存暴涨问题。</item>
	///             <item>winfrom客户端提供一个曲线控件，方便显示实时数据，多曲线数据。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-02-05" version="4.3.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>优化modbus tcp客户端的访问类，支持服务器返回错误信息。</item>
	///             <item>优化曲线控件，支持横轴文本显示，支持辅助线标记，详细见对应博客。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-02-22" version="4.3.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>曲线控件最新时间显示BUG修复。</item>
	///             <item>Modbus tcp错误码BUG修复。</item>
	///             <item>三菱访问类完善long类型读写。</item>
	///             <item>西门子访问类支持1500系列，支持读取订货号。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-03-05" version="4.3.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>曲线控件增加一个新的属性，图标标题。</item>
	///             <item>Modbus tcp服务器端的读写BUG修复。</item>
	///             <item>西门子访问类重新支持200smart。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-03-07" version="4.3.4" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Json组件更新至11.0.1版本。</item>
	///             <item>紧急修复日志类的BeforeSaveToFile事件在特殊情况的触发BUG。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-03-19" version="4.3.5" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复Modbus-tcp服务器接收异常的BUG。</item>
	///             <item>修复SoftBasic.ByteTo[U]ShortArray两个方法异常。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-04-05" version="5.0.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>网络核心层重新开发，完全的基于异步IO实现。</item>
	///             <item>所有双模式客户端类进行代码重构，接口统一。</item>
	///             <item>完善并扩充OperateResult对象的类型支持。</item>
	///             <item>提炼一些基础的更加通用的接口方法，在SoftBasic里面。</item>
	///             <item>支持欧姆龙PLC的数据交互。</item>
	///             <item>支持三菱的1E帧数据格式。</item>
	///             <item>不兼容升级，谨慎操作。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-04-10" version="5.0.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>OperateResult静态方法扩充。</item>
	///             <item>文件引擎提升缓存空间到100K，加速文件传输。</item>
	///             <item>三菱添加读取单个bool数据。</item>
	///             <item>Modbus-tcp客户端支持配置起始地址不是0的服务器。</item>
	///             <item>其他代码优化。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-04-14" version="5.0.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>ComplexNet服务器代码精简优化，移除客户端的在线信息维护代码。</item>
	///             <item>西门子访问类第一次握手信号18字节改为0x02。</item>
	///             <item>更新JSON组件到11.0.2版本。</item>
	///             <item>日志存储类优化，支持过滤存储特殊关键字的日志。</item>
	///             <item>Demo项目新增控件介绍信息。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-04-20" version="5.0.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复Modbus-Tcp服务器的空异常。</item>
	///             <item>修复西门子类写入float，double，long数据异常。</item>
	///             <item>修复modbus-tcp客户端读写字符串颠倒异常。</item>
	///             <item>修复三菱多读取数据字节的问题。</item>
	///             <item>双模式客户端新增异形客户端模式，变成了三模式客户端。</item>
	///             <item>提供异形modbus服务器和客户端Demo方便测试。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-04-25" version="5.0.4" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Modbus-tcp服务器同时支持RTU数据交互。</item>
	///             <item>异形客户端新增在线监测，自动剔除访问异常设备。</item>
	///             <item>modbus-tcp支持读取输入点。</item>
	///             <item>所有客户端设备的连接超时判断增加休眠，降低CPU负载。</item>
	///             <item>西门子批量读取上限为19个数组。</item>
	///             <item>其他小幅度的代码优化。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-04-30" version="5.0.5" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Modbus相关的代码优化。</item>
	///             <item>新增Modbus-Rtu客户端模式，配合服务器的串口支持，已经可以实现电脑本机的通讯测试了。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-05-04" version="5.0.6" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>提炼数据转换基类，优化代码，修复WordReverse类对字符串的BUG，相当于修复modbus和omron读写字符串的异常。</item>
	///             <item>新增一个全新的功能类，数据的推送类，轻量级的高效的订阅发布数据信息。具体参照Demo。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-05-07" version="5.0.7" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Modbus服务器提供在线客户端数量属性。</item>
	///             <item>所有服务器基类添加端口缓存。</item>
	///             <item>双模式客户端完善连接失败，请求超时的消息提示。</item>
	///             <item>修复双模式客户端某些特殊情况下的头子节NULL异常。</item>
	///             <item>修复三菱交互类的ASCII协议下的写入数据异常。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-05-12" version="5.0.8" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>新增一个埃夫特机器人的数据访问类。</item>
	///             <item>双模式客户端的长连接支持延迟连接操作，通过一个新方法完成。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-05-21" version="5.0.9" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>优化ComplexNet客户端的代码。</item>
	///             <item>更新埃夫特机器人的读取机制到最新版。</item>
	///             <item>Modbus Rtu及串口基类支持接收超时时间设置，不会一直卡死。</item>
	///             <item>Modbus Tcp及Rtu都支持带功能码输入，比如读取100地址，等同于03X100。（注意：该多功能地址仅仅适用于Read及相关的方法</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-05-22" version="5.0.10" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Modbus Tcp及Rtu支持手动更改站号。也就是支持动态站号调整。</item>
	///             <item>修复上个版本遗留的Modbus在地址偏移情况下会多减1的BUG。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-06-05" version="5.1.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Modbus服务器支持串口发送数据时也会触发消息接收。</item>
	///             <item>IReadWriteNet接口新增Read(string address,ushort length)方法。</item>
	///             <item>提炼统一的设备基类，支持Read方法及其扩展的子方法。</item>
	///             <item>修复埃夫特机器人的读取BUG。</item>
	///             <item>三菱PLC支持读取定时器，计数器的值，地址格式为"T100"，"C100"。</item>
	///             <item>新增快速离散的傅立叶频谱变换算法，并在Demo中测试三种周期信号。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-06-16" version="5.1.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复西门子fetch/write协议对db块，定时器，计数器读写的BUG。</item>
	///             <item>埃夫特机器人修复tostring()的方法。</item>
	///             <item>modbus客户端新增两个属性，指示是否字节颠倒和字符串颠倒，根据不同的服务器配置。</item>
	///             <item>IReadWriteNet接口补充几个数组读取的方法。</item>
	///             <item>新增一个全新的连接池功能类，详细请参见 https://www.cnblogs.com/dathlin/p/9191211.html </item>
	///             <item>其他的小bug修复，细节优化。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-06-27" version="5.1.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>IByteTransform接口新增bool[]数组转换的2个方法。</item>
	///             <item>Modbus Server类新增离散输入数据池和输入寄存器数据池，可以在服务器端读写，在客户端读。</item>
	///             <item>Modbus Tcp及Modbus Rtu及java的modbus tcp支持富地址表示，比如"s=2;100"为站号2的地址100信息。</item>
	///             <item>Modbus Server修复一个偶尔出现多次异常下线的BUG。</item>
	///             <item>其他注释修正。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-07-13" version="5.1.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Modbus服务器新增数据大小端配置。</item>
	///             <item>Modbus服务器支持数据存储本地及从本地加载。</item>
	///             <item>修复modbus服务器边界读写bug。</item>
	///             <item>ByteTransformBase的double转换bug修复。</item>
	///             <item>修复ReverseWordTransform批量字节转换时隐藏的一些bug。</item>
	///             <item>SoftBasic移除2个数据转换的方法。</item>
	///             <item>修复modbus写入单个寄存器的高地位倒置的bug。</item>
	///             <item>修复串口通信过程中字节接收不完整的异常。包含modbus服务器和modbus-rtu。</item>
	///             <item>添加了.net 4.5项目，并且其他项目源代码引用该项目。添加了单元测试，逐步新增测试方法。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-07-27" version="5.2.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>项目新增api文档，提供离线版和在线版，文档提供了一些示例代码。</item>
	///             <item>modbus-rtu新增批量的数组读取方法。</item>
	///             <item>modbus-rtu公开ByteTransform属性，方便的进行数据转换。</item>
	///             <item>SoftMail删除发送失败10次不能继续发送的机制。</item>
	///             <item>modbus server新增站号属性，站号不对的话，不响应rtu反馈。</item>
	///             <item>modbus server修复读取65524和65535地址提示越界的bug。</item>
	///             <item>Demo项目提供了tcp/ip的调试工具。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-08-08" version="5.2.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>API文档中西门子FW协议示例代码修复。</item>
	///             <item>modbus-rtu修复读取线圈和输入线圈的值错误的bug。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-08-23" version="5.2.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Demo中三菱A-1E帧，修复bool读取显示失败的BUG。</item>
	///             <item>数据订阅类客户端连接上服务器后，服务器立即推送一次。</item>
	///             <item>串口设备基类代码提炼，提供了多种数据类型的读写支持。</item>
	///             <item>仪表盘新增属性IsBigSemiCircle，设置为true之后，仪表盘可显示大于半圆的视图。</item>
	///             <item>提供了一个新的三菱串口类，用于采集FX系列的PLC，MelsecFxSerial</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-08-24" version="5.2.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复双模式基类的一个bug，支持不接受反馈数据。</item>
	///             <item>修复三菱串口类的读写bug，包括写入位，和读取字和位。</item>
	///             <item>相关代码重构优化。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-09-08" version="5.3.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>串口基类接收数据优化，保证接收一次完整的数据内容。</item>
	///             <item>新增一个容器罐子的控件，可以调整背景颜色。</item>
	///             <item>OperateResult成功时的错误码调整为0。</item>
	///             <item>修复modbus-tcp及modbus-rtu读取coil及discrete的1个位时解析异常的bug。</item>
	///             <item>授权类公开一个属性，终极秘钥的属性，感谢 洛阳-LYG 的建议。</item>
	///             <item>修复transbool方法在特殊情况下的bug</item>
	///             <item>NetworkDeviceBase 写入的方法设置为了虚方法，允许子类进行重写。</item>
	///             <item>SoftBasic: 新增三个字节处理的方法，移除前端字节，移除后端字节，移除两端字节。</item>
	///             <item>新增串口应用的LRC校验方法。还未实际测试。</item>
	///             <item>Siemens的s7协议支持V区自动转换，方便数据读取。</item>
	///             <item>新增ab plc的类AllenBradleyNet，已测试读写，bool写入仍存在一点问题。</item>
	///             <item>新增modbus-Ascii类，该类库还未仔细测试。</item>
	///             <item>埃夫特机器人更新，适配最新版本数据采集。</item>
	///             <item>其他的代码优化，重构精简</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-09-10" version="5.3.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复埃夫特机器人读取数据的bug，已测试通过。</item>
	///             <item>ByteTransform数据转换层新增一个DataFormat属性，可选ABCD,BADC,CDAB,DCBA</item>
	///             <item>三个modbus协议均适配了ByteTransform并提供了直接修改的属性，默认ABCD</item>
	///             <item>注意：如果您的旧项目使用的Modbus类，请务必重新测试适配。给你带来的不便，敬请谅解。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-09-21" version="5.3.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>所有显示字符串支持中英文，支持切换，默认为系统语言。</item>
	///             <item>Json组件依赖设置为不依赖指定版本。</item>
	///             <item>modbus-ascii类库测试通过。</item>
	///             <item>新增松下的plc串口读写类，还未测试。</item>
	///             <item>西门子s7类写入byte数组长度不受限制，原先大概250个字节左右。</item>
	///             <item>demo界面进行了部分的中英文适配。</item>
	///             <item>OperateResult类新增了一些额外的构造方法。</item>
	///             <item>SoftBasic新增了几个字节数组操作相关的通用方法。</item>
	///             <item>其他大量的细节的代码优化，重构。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-09-27" version="5.3.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>DeviceNet层添加异步的API，支持async+await调用。</item>
	///             <item>java修复西门子的写入成功却提示失败的bug。</item>
	///             <item>java代码重构，和C#基本保持一致。</item>
	///             <item>python版本发布，支持三菱，西门子，欧姆龙，modbus，数据订阅，同步访问。</item>
	///             <item>其他的代码优化，重构精简。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-10-20" version="5.4.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>python和java的代码优化，完善，添加三菱A-1E类。</item>
	///             <item>修复仪表盘控件，最大值小于0会产生的特殊Bug。</item>
	///             <item>NetSimplifyClient: 提供高级.net的异步版本方法。</item>
	///             <item>serialBase: 新增初始化和结束的保护方法，允许重写实现额外的操作。</item>
	///             <item>softBuffer: 添加一个线程安全的buffer内存读写。</item>
	///             <item>添加西门子ppi协议类，针对s7-200，需要最终测试。</item>
	///             <item>Panasonic: 修复松下plc的读取读取数据异常。</item>
	///             <item>修复fx协议批量读取bool时意外的Bug。</item>
	///             <item>NetSimplifyClient: 新增带用户int数据返回的读取接口。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-10-24" version="5.4.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>新增一个温度采集模块的类，基于modbus-rtu实现，阿尔泰科技发展有限公司的DAM3601模块。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-10-25" version="5.4.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>三菱的mc协议新增支持读取ZR文件寄存器功能。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-10-30" version="5.4.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复AB PLC的bool和byte写入失败的bug，感谢 北京-XLang 提供的思路。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-11-1" version="5.5.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>新增西门子PPI通讯类库，支持200，200smart等串口通信，感谢 合肥-加劲 和 江阴-  ∮溪风-⊙_⌒ 的测试</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-11-5" version="5.5.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>新增三菱计算机链接协议通讯库，支持485组网，有效距离达50米，感谢珠海-刀客的测试。</item>
	///             <item>串口协议的基类提供了检测当前串口是否处于打开的方法接口。</item>
	///             <item>西门子S7协议新增槽号为3的s7-400的PLC选项，等待测试。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-11-9" version="5.5.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>西门子PPI写入bool方法名重载到了Write方法里。</item>
	///             <item>松下写入bool方法名重载到了Write方法里。</item>
	///             <item>修复CRC16验证码在某些特殊情况下的溢出bug。</item>
	///             <item>西门子类添加槽号和机架号属性，只针对400PLC有效，初步测试可读写。</item>
	///             <item>ab plc支持对数组的读写操作，支持数组长度为0-246，超过246即失败。</item>
	///             <item>三菱的编程口协议修复某些特殊情况读取失败，却提示成功的bug。</item>
	///             <item>串口基类提高缓存空间到4096，并在数据交互时捕获COM口的异常。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-11-16" version="5.6.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复欧姆龙的数据格式错误，修改为CDAB。</item>
	///             <item>新增一个瓶子的控件。</item>
	///             <item>新增一个管道的控件。</item>
	///             <item>初步新增一个redis的类，初步实现了读写关键字。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-11-21" version="5.6.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>AB PLC读取数组过长时提示错误信息。</item>
	///             <item>正式发布redis客户端，支持一些常用的操作，并提供一个浏览器。博客：https://www.cnblogs.com/dathlin/p/9998013.html </item>
	///         </list>
	///     </revision>
	///     <revision date="2018-11-24" version="5.6.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>曲线控件的曲线支持隐藏其中的一条或是多条曲线，可以用来实现手动选择显示曲线的功能。</item>
	///             <item>Redis功能块代码优化，支持通知服务器进行数据快照保存，包括同步异步。</item>
	///             <item>Redis新增订阅客户端类，可以实现订阅一个或是多个频道数据。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-11-30" version="5.6.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>串口数据接收的底层机制重新设计。</item>
	///             <item>串口底层循环验证缓冲区是否有数据的间隔可更改，默认20ms。</item>
	///             <item>串口底层新增一个清除缓冲区数据的方法。</item>
	///             <item>串口底层新增一个属性，用于配置是否在每次读写前清除缓冲区的脏数据。</item>
	///             <item>新增了一个SharpList类，用于超高性能的管理固定长度的数组。博客：https://www.cnblogs.com/dathlin/p/10042801.html </item>
	///         </list>
	///     </revision>
	///     <revision date="2018-12-3" version="5.6.4" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Networkbase: 接收方法的一个多余对象删除。</item>
	///             <item>修复UserDrum控件的默认的text生成，及复制问题。</item>
	///             <item>UserDrum修复属性在设计界面没有注释的bug。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-12-5" version="5.6.5" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复Demo程序在某些特殊情况下无法在线更新的bug。</item>
	///             <item>修复曲线控件隐藏曲线时在某些特殊情况的不隐藏的bug。</item>
	///             <item>modbus协议无论读写都支持富地址格式。</item>
	///             <item>修复连接池清理资源的一个bug，感谢 泉州-邱蕃金</item>
	///             <item>修复java的modbus代码读取线圈异常的操作。</item>
	///             <item>Demo程序新增免责条款。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-12-11" version="5.6.6" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复redis客户端对键值进行自增自减指令操作时的类型错误bug。</item>
	///             <item>修复redis客户端对哈希值进行自增自减指令操作时的类型错误bug。</item>
	///             <item>推送的客户端可选委托或是事件的方式，方便labview调用。</item>
	///             <item>推送的客户端修复当服务器的关键字不存在时连接未关闭的Bug。</item>
	///             <item>Demo程序里，欧姆龙测试界面新增数据格式功能。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-12-19" version="5.6.7" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>ByteTransfer数据转换类新增了一个重载的构造方法。</item>
	///             <item>Redis客户提供了一个写键值并发布订阅的方法。</item>
	///             <item>AB-PLC支持槽号选择，默认为0。</item>
	///             <item>PushNet推送服务器新增一个配置，可用于设置是否在客户端刚上线的时候推送缓存数据。</item>
	///             <item>PushNet推送服务器对客户端的上下限管理的小bug修复。</item>
	///             <item>本版本开始，组件将使用强签名。</item>
	///             <item>本版本开始，组件的控件库将不再维护更新，所有的控件在新的控件库重新实现和功能增强，VIP群将免费使用控件库。</item>
	///             <item>VIP群的进入资格调整为赞助200Rmb，谢谢支持。</item>
	///         </list>
	///     </revision>
	///     <revision date="2018-12-27" version="5.7.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复modbus服务器地址写入的bug，之前写入地址数据后无效，必须带x=3;100才可以。</item>
	///             <item>修复极少数情况内核对象申请失败的bug，之前会引发资源耗尽的bug。</item>
	///             <item>SoftBasic的ByteToBoolArray新增一个转换所有位的重载方法，不需要再传递位数。</item>
	///             <item>埃夫特机器人新增旧版的访问类对象，达到兼容的目的。</item>
	///             <item>Demo程序新增作者简介。</item>
	///             <item>修复Demo程序的redis订阅界面在设置密码下无效的bug。</item>
	///             <item>Demo程序的免责界面新增demo在全球的使用情况。</item>
	///             <item>VIP群将免费使用全新的控件库，谢谢支持。地址：https://github.com/dathlin/HslControlsDemo </item>
	///         </list>
	///     </revision>
	///     <revision date="2018-12-31" version="5.7.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复modbus服务器地址读取的bug，之前读取地址数据后无效，必须带x=3;100才可以。</item>
	///             <item>NetPush功能里，当客户端订阅关键字时，服务器即使没有该关键字，也成功。</item>
	///             <item>三菱的通讯类支持所有的字读取。例如读取M100的short数据表示M100-M115。</item>
	///             <item>VIP群将免费使用全新的控件库，谢谢支持。地址：https://github.com/dathlin/HslControlsDemo </item>
	///         </list>
	///     </revision>
	///     <revision date="2019-1-15" version="5.7.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复三菱A-1E协议的读取数据的BUG错误，给大家造成的不便，非常抱歉。</item>
	///             <item>VIP群将免费使用全新的控件库，谢谢支持。地址：https://github.com/dathlin/HslControlsDemo </item>
	///         </list>
	///     </revision>
	///     <revision date="2019-2-7" version="5.7.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>欧姆龙读写机制更改，报警的异常不再视为失败，仍然可以解析数据。</item>
	///             <item>Modbus地址优化，Modbus服务器的地址读写优化。</item>
	///             <item>新增一个数据池类，SoftBuffer，主要用来缓存字节数组内存的，支持BCL数据类型读写。</item>
	///             <item>Modbus服务器的数据池更新，使用了最新的数据池类SoftBuffer。</item>
	///             <item>SoftBasic类新增一个GetEnumFromString方法，支持从字符串直接生成枚举值，已通过单元测试。</item>
	///             <item>新增一个机器人的读取接口信息IRobotNet，统一化所有的机器人的数据读取。</item>
	///             <item>Demo程序中增加modbus的服务器功能。</item>
	///             <item>VIP群将免费使用全新的控件库，谢谢支持。地址：https://github.com/dathlin/HslControlsDemo </item>
	///         </list>
	///     </revision>
	///     <revision date="2019-2-13" version="5.7.4" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>日志存储的线程号格式化改为D3，也即三位有效数字。</item>
	///             <item>日志存储事件BeforeSaveToFile里允许设置日志Cancel属性，强制当前的记录不存储。</item>
	///             <item>JSON库更新到12.0.1版本。</item>
	///             <item>SoftBasic新增一个GetTimeSpanDescription方法，用来将时间差转换成文本的方法。</item>
	///             <item>调整日志分析控件不随字体变化而变化。</item>
	///             <item>其他的代码精简优化。</item>
	///             <item>VIP群将免费使用全新的控件库，谢谢支持。地址：https://github.com/dathlin/HslControlsDemo </item>
	///         </list>
	///     </revision>
	///     <revision date="2019-2-21" version="5.8.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>SoftBasic修复AddArrayData方法批量添加数据异常的bug，导致曲线控件显示异常。</item>
	///             <item>提炼一个公共的欧姆龙辅助类，准备为串口协议做基础的通用支持。</item>
	///             <item>RedisHelper类代码优化精简，提炼部分的公共逻辑到NetSupport。</item>
	///             <item>SoftBuffer: 新增读写单个的位操作，通过位的与或非来实现。</item>
	///             <item>SiemensS7Server：新增一个s7协议的服务器，可以模拟PLC，进行通讯测试或是虚拟开发。</item>
	///             <item>其他的代码精简优化。</item>
	///             <item>VIP群将免费使用全新的控件库，谢谢支持。地址：https://github.com/dathlin/HslControlsDemo </item>
	///         </list>
	///     </revision>
	///     <revision date="2019-3-4" version="6.0.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>西门子虚拟PLC的ToString()方法重新实现。</item>
	///             <item>埃夫特机器人的json格式化修正换行符。</item>
	///             <item>IReadWriteNet接口添加Write(address, bytes)的方法。</item>
	///             <item>Modbus虚拟服务器修复写入位操作时影响后面3个位的bug。</item>
	///             <item>SoftBuffer内存数据池类的SetValue(byte,index)的bug修复。</item>
	///             <item>西门子虚拟PLC和Modbus服务器新增客户端管理，关闭时也即断开所有连接。</item>
	///             <item>三菱编程口协议的读取结果添加错误说明，显示原始返回信号，便于分析。</item>
	///             <item>三菱MC协议新增远程启动，停止，读取PLC型号的接口。</item>
	///             <item>新增三菱MC协议的串口的A-3C协议支持，允许读写三菱PLC的数据。</item>
	///             <item>新增欧姆龙HostLink协议支持，允许读写PLC数据。</item>
	///             <item>新增基恩士PLC的MC协议支持，包括二进制和ASCII格式，支持读写PLC的数据。</item>
	///             <item>所有PLC的地址说明重新规划，统一在API文档中查询。</item>
	///             <item>注意：三菱PLC的地址升级，有一些地址格式进行了更改，比如定时器和计数器，谨慎更新，详细地址参考最新文档。</item>
	///             <item>如果有公司使用了本库并愿意公开logo的，将在官网及git上进行统一显示，有意愿的联系作者。</item>
	///             <item>VIP群将免费使用全新的控件库，谢谢支持。地址：https://github.com/dathlin/HslControlsDemo </item>
	///         </list>
	///     </revision>
	///     <revision date="2019-3-10" version="6.0.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复代码注释上的一些bug，三菱的注释修复。</item>
	///             <item>调整三菱和基恩士D区数据和W区数据的地址范围，原来只支持到65535。</item>
	///             <item>SoftIncrementCount: 修复不持久化的序号自增类的数据复原的bug，并添加totring方法。</item>
	///             <item>IRobot接口更改。针对埃夫特机器人进行重新实现。</item>
	///             <item>RedisClient: 修复redis类在带有密码的情况下锁死的bug。</item>
	///             <item>初步添加Kuka机器人的通讯类，等待测试。</item>
	///             <item>西门子的s7协议读写字符串重新实现，根据西门子的底层存储规则来操作。</item>
	///             <item>Demo的绝大多的界面进行重构。更友好的支持英文版的显示风格。</item>
	///             <item>如果有公司使用了本库并愿意公开logo的，将在官网及git上进行统一显示，有意愿的联系作者。</item>
	///         </list>
	///     </revision>
	///     <revision date="2019-3-21" version="6.0.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复西门子s7协议读写200smart字符串的bug。</item>
	///             <item>重构优化NetworkBase及NetwordDoubleBase网络类的代码。</item>
	///             <item>新增欧姆龙的FinsUdp的实现，DA1【PLC节点号】在配置Ip地址的时候自动赋值，不需要额外配置。</item>
	///             <item>FinsTcp类的DA1【PLC节点号】在配置Ip地址的时候自动赋值，不需要额外配置。</item>
	///         </list>
	///     </revision>
	///     <revision date="2019-3-28" version="6.0.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>NetPushServer推送服务器修复某些情况下的推送卡死的bug。</item>
	///             <item>SoftBuffer内存数据类修复Double转换时出现的错误bug。</item>
	///             <item>修复Kuka机器人读写数据错误的bug，已通过测试。</item>
	///             <item>修复三菱的MelsecMcAsciiNet类写入bool值及数组会导致异常的bug，已通过单元测试。</item>
	///             <item>SoftBasic新增从字符串计算MD5码的方法。</item>
	///         </list>
	///     </revision>
	///     <revision date="2019-4-4" version="6.0.4" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复java的NetPushClient掉线重复连接的bug。</item>
	///             <item>发布java的全新测试Demo。</item>
	///             <item>Kuka机器人Demo修改帮助链接。</item>
	///             <item>西门子新增s200的以太网模块连接对象。</item>
	///             <item>修复文件引擎在上传文件时意外失败，服务器仍然识别为成功的bug。</item>
	///         </list>
	///     </revision>
	///     <revision date="2019-4-17" version="6.1.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复日志存储自身异常时，时间没有初始化的bug。</item>
	///             <item>NetworkBase: 新增UseSynchronousNet属性，默认为true，通过同步的网络进行读写数据，异步手动设置为false。</item>
	///             <item>修复西门子的读写字符串的bug。</item>
	///             <item>添加KeyenceNanoSerial以支持基恩士Nano系列串口通信。</item>
	///             <item>其他的代码优化。</item>
	///             <item>发布一个基于xamarin的安卓测试demo。</item>
	///             <item>发布官方论坛： http://bbs.hslcommunication.cn/ </item>
	///         </list>
	///     </revision>
	///     <revision date="2019-4-24" version="6.1.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复基恩士MC协议读取D区数据索引不能大于100000的bug。</item>
	///             <item>修复基恩士串口协议读写bool数据的异常bug。</item>
	///             <item>修复数据推送服务器在客户端异常断开时的奔溃bug，界面卡死bug。</item>
	///             <item>SoftNumericalOrder类新增数据重置和，最大数限制 。</item>
	///             <item>ModbusTcp客户端公开属性SoftIncrementCount，可以强制消息号不变，或是最大值。</item>
	///             <item>NetworkBase: 异步的方法针对Net451及standard版本重写。</item>
	///             <term>modbus服务器的方法ReadFromModbusCore( byte[] modbusCore )设置为虚方法，可以继承重写，实现自定义返回。</term>
	///             <item>串口基类serialbase的初始化方法新增多个重载方法，方便VB和labview调用。</item>
	///             <item>NetworkBase: 默认的机制任然使用异步实现，UseSynchronousNet=false。</item>
	///             <item>发布官方论坛： http://bbs.hslcommunication.cn/ </item>
	///         </list>
	///     </revision>
	///     <revision date="2019-4-25" version="6.1.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>紧急修复在NET451和Core里的异步读取的bug。</item>
	///             <item>紧急修复PushNetServer的发送回调bug。</item>
	///             <item>发布官方论坛： http://bbs.hslcommunication.cn/ </item>
	///         </list>
	///     </revision>
	///     <revision date="2019-5-6" version="6.2.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>SoftBuffer缓存类支持bool数据的读写，bool数组的读写，并修复double读写的bug。</item>
	///             <item>Modbus虚拟服务器代码重构实现，继承自NetworkDataServerBase类。</item>
	///             <item>新增韩国品牌LS的Fast Enet协议</item>
	///             <item>新增韩国品牌LS的Cnet协议</item>
	///             <item>新增三菱mc协议的虚拟服务器，仅支持二进制格式的机制。</item>
	///             <item>LogNet支持写入任意的字符串格式。</item>
	///             <item>其他的注释添加及代码优化。</item>
	///             <item>发布官方论坛： http://bbs.hslcommunication.cn/ </item>
	///         </list>
	///     </revision>
	///     <revision date="2019-5-9" version="6.2.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复三菱读写PLC位时的bug。</item>
	///             <item>修复Modbus读写线圈及离散的变量bug。</item>
	///             <item>强烈建议更新，不能使用6.2.0版本！或是回退更低的版本。</item>
	///             <item>有问题先上论坛： http://bbs.hslcommunication.cn/ </item>
	///         </list>
	///     </revision>
	///     <revision date="2019-5-10" version="6.2.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复上个版本modbus的致命bug，已通过单元测试。</item>
	///             <item>新增松下的mc协议，demo已经新增，等待测试。</item>
	///             <item>github源代码里的支持的型号需要大家一起完善。</item>
	///         </list>
	///     </revision>
	///     <revision date="2019-5-31" version="6.2.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Ls的Fast Enet协议问题修复，感谢来自埃及朋友。</item>
	///             <item>Ls的CEnet协议问题修复，感谢来自埃及朋友。</item>
	///             <item>Ls新增虚拟的PLC服务器，感谢来自埃及朋友。</item>
	///             <item>改进了机器码获取的方法，获取实际的硬盘串号。</item>
	///             <item>日志的等级为None的情况，不再格式化字符串，原生写入日志。</item>
	///             <item>IReadWriteNet接口测试西门子的写入，没有问题。</item>
	///             <term>三菱及松下，基恩士的地址都调整为最大20亿长度，实际取决于PLC本身。</term>
	///             <item>松下MC协议修复LD数据库的读写bug。</item>
	///             <item>Redis的DEMO界面新增删除key功能。</item>
	///         </list>
	///     </revision>
	///     <revision date="2019-6-3" version="6.2.4" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Redis新增读取服务器的时间接口，可用于客户端的时间同步。</item>
	///         </list>
	///     </revision>
	///     <revision date="2019-6-6" version="6.2.5" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>西门子的SiemensS7Net类当读取PLC配置长度的DB块数据时，将提示错误信息。</item>
	///         </list>
	///     </revision>
	///     <revision date="2019-6-22 " version="7.0.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>新增安川机器人通信类，未测试。</item>
	///             <item>西门子的多地址读取的长度不再限制为19个，而是无限制个。</item>
	///             <item>NetworkDoubleBase: 实现IDispose接口，方便手动释放资源。</item>
	///             <item>SerialBase: 实现IDispose接口，方便手动释放资源。</item>
	///             <item>NetSimplifyClient:新增一个async...await方法。</item>
	///             <item>NetSimplifyClient:新增读取字符串数组。</item>
	///             <item>ModbusServer:新增支持账户密码登录，用于构建安全的服务器，仅支持hsl组件的modbus安全访问。</item>
	///             <item>NetSimplifyServer:新增支持账户密码登录。</item>
	///             <item>新增永宏PLC的编程口协议。</item>
	///             <item>新增富士PLC的串口通信，未测试。</item>
	///             <item>新增欧姆龙PLC的CIP协议通讯。</item>
	///             <item>初步添加OpenProtocol协议，还未完成，为测试。</item>
	///             <item>MelsecMcNet:字单位的批量读取长度突破960长度的限制，支持读取任意长度。</item>
	///             <item>MelsecMcAsciiNet:字单位的批量读取长度突破480长度的限制，支持读取任意长度。</item>
	///             <item>AllenBradleyNet:读取地址优化，支持读取数组任意起始位置，任意长度，支持结构体嵌套读取。</item>
	///             <item>其他大量的代码细节优化。</item>
	///         </list>
	///     </revision>
	///     <revision date="2019-6-25" version="7.0.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>IReadWriteNet完善几个忘记添加的Write不同类型参数的重载方法。</item>
	///             <item>IReadWriteNet新增ReadBool方法，Write(string address, bool value)方法，是否支持操作需要看plc是否支持，不支持返回操作不支持的错误。</item>
	///             <item>OmronFinsNet:新增一个属性，IsChangeSA1AfterReadFailed，当设置为True时，通信失败后，就会自动修改SA1的值，这样就能快速链接上PLC了。</item>
	///             <item>OmronFinsNet:新增读写E区的能力，地址示例E0.0，EF.100，E12.200。</item>
	///             <item>新增HslDeviceAddress特性类，现在支持直接基于对象的读写操作，提供了一种更加便捷的读写数据的机制，详细的关注后续的论坛。</item>
	///             <item>本组件的最后一个对个人免费的版本，企业使用一律需要授权，授权流程为签合同，付款，开票。</item>
	///         </list>
	///     </revision>
	///     <revision date="2019-9-10" version="8.0.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>SimpleHybirdLock: 简单混合锁的性能优化，基元对象采用懒加载的机制实现，同时增加了高级混合锁的类，支持自旋，线程拥有权，在高竞争的情况下性能大幅增加。</item>
	///             <item>NetSoftUpdateServer: 软件自动更新的服务器端支持传送指定目录下的文件及其子文件夹下的所有文件内容，都将更新到客户端的电脑上去。</item>
	///             <item>AllenBradleyNet: 修复字符串的读写bug，支持读写任意长度的字符串信息。</item>
	///             <item>MelsecFxSerial: 三菱编程口协议支持读写D1024以上地址的数据，感谢 厦门-Mr.T 的贡献。</item>
	///             <item>PIDHelper: 新增一个Pid的辅助类，用于模拟pid的波形情况。</item>
	///             <item>NetPushClient: 修改一个时间的注释，追加单位信息，时间的单位是毫秒。</item>
	///             <item>XGBFastEnet: 感谢埃及的朋友，修复了一些bug信息。</item>
	///             <item>MelsecFxSerialOverTcp: 新增基于网口透传的三菱的编程口通讯类。</item>
	///             <item>MelsecFxLinksOverTcp: 新增基于网口透传的三菱的计算机链接协议的通讯类。</item>
	///             <item>MelsecA3CNet1OverTcp: 新增基于网口透传的三菱的A-3C的协议的通讯类。</item>
	///             <item>OmronHostLinkOverTcp: 新增基于网口透传的欧姆龙的hostLink协议的通讯类。</item>
	///             <item>PanasonicMewtocolOverTcp: 新增基于网口透传的松下的Mewtocol协议的通讯类。</item>
	///             <item>SiemensPPIOverTcp: 新增基于网口透传的西门子PPi协议的通讯类。</item>
	///             <item>XGBCnetOverTcp: 新增基于网口透传的Lsis的XGBCnet协议的通讯类。</item>
	///             <item>KeyenceNanoSerialOverTcp: 新增基于网口透传的基恩士的NanoSerial串口协议的通讯类。</item>
	///             <item>FujiSPBOverTcp: 新增基于网口透传的富士的SPB串口协议的通讯类。</item>
	///             <term>FatekProgramOverTcp: 新增基于网口透传的永宏plc的串口协议的通讯类。</term>
	///             <item>ModbusRtuOverTcp: 新增基于网口透传的Modbus rtu协议的通讯类。</item>
	///             <item>Modbus相关的功能类进行代码精简，重构，优化，api标准化为ReadBool,WriteBool,Read,Write，移除了一些特殊的方法api，本次升级不兼容。</item>
	///             <item>FFTFilter: 新增一个基于FFT（快速离散傅立叶变换）的滤波功能，可以作为一个高级的曲线拟合方案，详细参照demo，感谢 北京-monk 网友的支持。</item>
	///             <item>KnxUdp: 新增一个KnxUdp的数据通讯类，感谢上海-null的支持。</item>
	///             <item>ABBWebApiClient: 新增ABB机器人的基于web api的访问机制的通讯类。</item>
	///             <item>SickIcrTcpServer: 新增一个sick的条码读取类，支持被动连接，主动连接，经过测试，同时支持海康，基恩士，DATELOGIC扫码器。</item>
	///             <item>Demo: Demo工具新增了一个基于tcp的服务器的测试界面。</item>
	///             <item>本组件从v8.0.0开始进入付费模式，谨慎升级，未激活的将只能使用8小时，普通vip群发放激活码，仅支持个人用途使用，禁止破解，感谢对正版的支持。</item>
	///             <item>今天是2019年9月10日，祝天下所有的教师节日快乐。</item>
	///         </list>
	///     </revision>
	///     <revision date="2019-9-17" version="8.0.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>所有网口透传类对象完善实例化的方法，都新增一个指定ip及端口的实例方法。</item>
	///             <item>ABBWebClient: 完善实例化方法，修改ToString的格式化内容，提炼了webapi的基类，开放ip地址和端口。</item>
	///             <item>ABBWebClient: 新增提供了机器人自身IO，扩展IO，最新的报警日志的数据读取API。</item>
	///             <item>NetSimplifyClient: 修复了当ReceiveTimeOut小于0，但是实际接收时会发生奔溃的bug。</item>
	///             <item>NetPlainSocket: 新增一个基于socket的明文的网络发送和接收类，采用事件驱动的机制。</item>
	///             <item>LogNet: 日志类对象新增一个特性，当日志的文件名设置为空的时候，将不会创建文件，仅仅触发 BeforSaveToFile 事件，方便日志显示。</item>
	///             <item>XGBCnet: Lsis的plc的串口类修复一个bug，感谢埃及朋友的贡献。</item>
	///             <item>SoftIncrementCount: 消息号自增类新增一个方法，重置当前的消息号。</item>
	///             <item>PanasonicMewtocol: 修复松下的串口类读写单个bool时异常的bug，地址支持字+位的表示方式，R33=R2.1，方便大家输入测试。</item>
	///             <item>MqttClient: 新增一个Mqtt协议的客户端类，支持用户名密码，支持发布，支持订阅，支持重连，欢迎一起测试。</item>
	///             <item>本组件从v8.0.0开始进入付费授权模式，详细参考官方：http://www.hslcommunication.cn 。</item>
	///         </list>
	///     </revision>
	///     <revision date="2019-9-19" version="8.0.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>ABBWebClient: abb机器人的api读取日志的接口新增一个参数，读取最近的日志数量。默认为10条。</item>
	///             <item>MQTTClient: 修复mqtt客户端类的消息重复bug，修复发送空订阅的bug。</item>
	///             <item>SiemensS7Net: 西门子的s7协议的类新增一个api，支持时间的读写，支持异步，时间格式和s7net一致。</item>
	///             <item>本组件从v8.0.0开始进入付费授权模式，详细参考官方：http://www.hslcommunication.cn 。</item>
	///         </list>
	///     </revision>
	///     <revision date="2019-9-26" version="8.0.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Networkbase: 修复套接字网络授权失败时不关闭网络的bug。</item>
	///             <item>SoftBasic: 新增一个数组数据格式化的方法信息。</item>
	///             <item>MqttServer: 新增一个mqtt的服务器，初步支持订阅，发布订阅，强制发布订阅，在线客户端数量功能等等。</item>
	///             <item>Demo: 所有的PLC的demo和modbus协议的demo，支持批量读取各种类型的数组数据。</item>
	///             <item>Nuget: 新增本项目的图标，在nuget上搜索时会显示图标。</item>
	///             <item>本组件从v8.0.0开始进入付费授权模式，详细参考官方：http://www.hslcommunication.cn 。</item>
	///         </list>
	///     </revision>
	///     <revision date="2019-10-7" version="8.1.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>ModbusUdp: 新增一个Modbus的基于udp的协议类，使用的tcp的报文的机制。</item>
	///             <item>HttpServer: 新增一个http的服务器封装类，方便实现基于webapi的后台功能，集成GET，POST的接口操作。</item>
	///             <item>Serial Ports: standard项目依赖官方串口库，实现所有的设备的串口支持，可应用于跨平台。</item>
	///             <item>standard: 在nuget上提供.net standard2.1版本的库。</item>
	///             <item>本组件从v8.0.0开始进入付费授权模式，详细参考官方：http://www.hslcommunication.cn 。</item>
	///         </list>
	///     </revision>
	///     <revision date="2019-10-11" version="8.1.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Lsis: 感谢埃及朋友的支持，修复了一些bug，支持了bool的操作。</item>
	///             <item>Redis: 新增db块属性设置，修复短连接下切换db块无效的bug，因为db块是跟随连接的。</item>
	///             <item>MQTT: 修复客户端和服务器的长度计算bug，支持和其他mqtt组件混合使用。</item>
	///             <item>MQTT Demo: 优化demo功能，支持文本追加或是覆盖选择，文本格式化查看选择。</item>
	///             <item>Http Server: 支持跨域属性选择，编码统一为utf-8，兼容浏览器和postman，demo中增加返回类型示例。</item>
	///             <item>Modbus server及Lsis Server: 针对.net standard版本，开放串口。</item>
	///             <item>本组件从v8.0.0开始进入付费授权模式，详细参考官方：http://www.hslcommunication.cn 。</item>
	///         </list>
	///     </revision>
	///     <revision date="2019-10-16" version="8.1.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Lsis: 感谢埃及朋友的支持，demo增加了bool操作。</item>
	///             <item>Knx驱动：增加测试demo，完善驱动，测试通过，有需要的朋友可以查看信息。</item>
	///             <item>IntegrationFileClient: 完善文件的收发类，新增重载的构造方法，传入ip地址及端口即可。</item>
	///             <item>melsec: 三菱的MC协议部分错误代码关联了文本信息，在测试的时候即可弹出错误信息，方便排查，常见了已经绑定。</item>
	///             <item>melsec: 新增3e协议的随机字批量读取操作，支持跨地址，跨数据类型混合交叉读取，一次即可读完。</item>
	///             <item>fileserver: 修复linux下的bug，新增上传文件后的触发事件，将文件的信息都传递给调用者。</item>
	///             <item>SiemensMpi: 添加MPI协议，并完善demo，等待测试。</item>
	///             <item>本组件从v8.0.0开始进入付费授权模式，详细参考官方：http://www.hslcommunication.cn 。</item>
	///         </list>
	///     </revision>
	///     <revision date="2019-10-24" version="8.1.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Lsis: 感谢埃及朋友的支持，demo完善了cpu类型的选择。</item>
	///             <item>LogNet:新增移除关键字的接口方法，修复linux运行路径解析的bug，完善api文档的示例代码。</item>
	///             <item>大量的细节优化，变量名称单次拼写错误的修复。</item>
	///             <item>Modbus: 当地址为x=3;100时，读正常，写入异常的问题修复，功能码自动替换为0x10。</item>
	///             <item>FileNet: 修复高并发下载时的下载异常的问题，调整指令头的超时时间。</item>
	///             <item>AB plc: 公开一个新的api接口，运行配置一些比较高级的数据。</item>
	///             <item>接下来计划：1.完善hsl的demo，api文档，准备基础的入门视频；2.开始完善java版本的代码，java版本只对超级VIP群开放。</item>
	///             <item>本组件从v8.0.0开始进入付费授权模式，详细参考官方：http://www.hslcommunication.cn 。</item>
	///         </list>
	///     </revision>
	///     <revision date="2019-12-3" version="8.2.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>三菱的MC协议支持读取SM和SD，特殊连接继电器，特殊寄存器。</item>
	///             <item>PushNet优化相关代码。</item>
	///             <item>MelsecMcUdp: 新增三菱的MC协议的UDP通讯类。</item>
	///             <item>MelsecMcAsciiUdp: 新增三菱的MC协议的ASCII格式的UDP通讯类。</item>
	///             <item>MelsecMcServer: 三菱的虚拟服务器修复数据存储加载的bug。</item>
	///             <item>Serial: 串口的基类公开了一个Rts属性，用于某些串口无法读取的设备的情况。</item>
	///             <item>OmronFinsServer: 新增欧姆龙的虚拟plc，支持和hsl自身的通讯，支持cio，h区，ar区，d区的通信，不支持E区。</item>
	///             <item>AllenBradleyServer: 新增ab plc的虚拟plc，支持和hsl的自身的通讯，在demo里预设了4个变量值。不支持结构体和二维及以上数组读写。</item>
	///             <item>Aline: 异形的服务器对象新增一个设置属性，是否反馈注册结果，默认为True。</item>
	///             <item>SoftBasic: 数组格式化操作新增格式化的字符串说明。</item>
	///             <item>Modbus: 调整Write( string address, bool value )采用05功能码写入，而参数为bool[]的话，采用0F功能码。</item>
	///             <item>Modbus: 提供WriteOneRegister方法，写入单个的寄存器，使用06功能码。</item>
	///             <item>LogNet: 日志在实例化的时候，添加对当前设置的目录的是否存在的检查，如果不存在，则自动创建目录。</item>
	///             <item>Python: 大量代码更新，新增了一个欧姆龙的fins-tcp通信。</item>
	///             <item>Java: 大量代码更新，新增了一个AB plc的读写类。</item>
	///         </list>
	///     </revision>
	///     <revision date="2019-12-11" version="8.2.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Cip协议：cip协议开放Eip指令自定义输入，优化指令生成算法。</item>
	///             <item>Cip协议：Write(string address, byte[] data)方法提示使用WriteTag信息。</item>
	///             <item>NetworkDoubleBase: 修复bool异步读写提示不支持的bug，现在可以使用异步了。</item>
	///             <item>SAMSerial：新增身份证阅读器的串口协议，支持读取身份证信息，头像信息还未解密。</item>
	///             <item>SAMTcpNet：新增身份证阅读器的串口透传协议，支持读取身份证信息，头像信息还未解密。</item>
	///             <item>BeckhoffAdsNet：新增倍福plc的协议，还未通过测试。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-1-3" version="8.2.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>lsis的plc优化，感谢埃及朋友的提供的技术支持。</item>
	///             <item>Panasonic: 松下的Mewtocol协议增加SR区的支持，解析地址的方法修改为Public，方便外面调用。</item>
	///             <item>Panasonic: 松下的Mewtocol协议批量读取bool方法是按字为单位，读取长度按照位为单位，地址写Y0，Y1，不能写Y0.4。</item>
	///             <item>ab-plc: 虚拟服务器修复上个版本造成的bug，导致读写数据成功，但是数据实际没有更改。</item>
	///             <item>ab-plc: 支持超长的数组读取，可以一次性读取任意长度的数组内容，不再需要手动切片。</item>
	///             <item>ab-plc: 新增一个api接口，可以遍历所有的ab-plc的变量名称。</item>
	///             <item>beckoff: 倍福的plc通信通过测试，需要设置正确各种网络号才可以，优化了标签缓存。</item>
	///             <item>java: java版本的ab-plc类优化，支持超长的数组读取。</item>
	///             <item>python: python版本的代码新增ab-plc的读取类。</item>
	///             <item>demo: 安卓的demo增加lsis，mqtt协议的界面。</item>
	///             <item>Melsec: 三菱PLC的多块批量读取目前只支持字地址，后续继续优化。</item>
	///             <item>其他的代码优化和重构。</item>
	///             <item>java版本的源代码及demo，python版本的源代码及demo仅对商业授权用户开放，谢谢支持。</item>
	///             <item>作者于2020年1月5日和王女士结婚，地址是浙江省金华市兰溪市马涧镇，欢迎有空的老铁们来坐坐，带红包就行。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-2-13" version="9.0.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>宣布V9版本脱胎换骨，浴火重生，C#版本的组件库底层网络大幅重构，删除一直以来的伪异步，原先的通过改为纯同步，并从底层提供完整的异步方法。</item>
	///             <item>注意：不兼容升级，影响范围，MQTT协议的事件，网络的同步设置，西门子的PPI协议取消WriteByte方法，改为和其他一样的Write(string address,byte value)重载了，升级请谨慎测试。</item>
	///             <item>所有的PLC通讯类，机器人类通讯类，Modbus通讯类，身份证类，包括 IReadWriteNet 接口都实现了异步的操作，针对NET45以上及Standard平台。</item>
	///             <item>MQTT协议修改触发的消息事件，返回session信息，支持自定义返回数据信息，支持当前消息的发布拦截操作，服务器主送发布的消息支持是否驻留，默认主题驻留。</item>
	///             <item>新增websocket协议的服务器，客户端，问答客户端。支持直接从C#的后台发送数据到网页前端，支持订阅操作。详细见demo的操作。</item>
	///             <item>ComplexNet,SimplifyNet,PushNet,FileNet这几个网络引擎代码优化，初步测试OK。</item>
	///             <item>SoftBasic: 新增方法SpliceStringArray，用来合并字符串信息。增加了ByteToHexString的空校验。</item>
	///             <item>HttpServer: 异步优化，修复读取数据时可能长度不足的bug。</item>
	///             <item>YRC1000: 安川机器人修改无法读取的bug，目前已经测试通过，感谢网友的支持。</item>
	///             <item>Java: 修复ab-plc读取失败的错误信息，原因来自一个强制转换失败的错误。</item>
	///             <item>本版本改动较多，尽管我已经仔细测试过，但是仍然不可避免存在一些bug，欢迎大家使用，测试，有问题可以报告给我，相信hsl组件会变的更加强大。</item>
	///             <item>本版本依然是商业授权的，如果需要测试，可以付费240rmb，加入vip群，可以将hsl用于测试环境和研究学术用途，欢迎大家加我的支付宝好友，hsl200909@163.com </item>
	///             <item>加油，武汉！加油，中国！疫情之后，无人自动化工厂将会获得更大的关注和发展，我辈当自强。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-2-19" version="9.0.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>底层的网络在对方关闭连接后，不再等待接收，直接返回对方已关闭的错误信息，提供通信的性能。</item>
	///             <item>四个服务器类，complexserver, simplifyserver,mqttserver,websocketserver开发关闭客户端连接的方法，调用者可以手动操作关闭。</item>
	///             <item>MQTT服务器新增一个客户端上线事件，包含客户端的会话参数，方便实现一些特殊的场景需求，在api文档中增加调用示例。</item>
	///             <item>Websocket服务器新增一个客户端上线事件，包含客户端的会话参数，方便实现一些特殊的场景需求。</item>
	///             <item>Websocket服务器添加0x0A的心跳返回，用于有些客户端的心跳验证操作。</item>
	///             <item>RedisClient: redis相关的代码优化，调整，添加了异步api接口，本机性能测试不如同步，有待优化。</item>
	///             <item>RedisClient: 新增基于特性的读写，自动组合键批量读取，组合哈希键批量读取操作，提升性能，详细参考api文档。写入操作不支持列表相关的特性。</item>
	///             <item>Demo的写入byte操作的反射代码获取失败的bug修复。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-2-25" version="9.0.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复websocket对某些浏览器的请求验证失败的bug，改为正则表达式验证，适用的范围更加广阔。</item>
	///             <item>三菱的mc协议的错误信息更加明确化，将提供更加确切的错误描述，方便大家查找错误。</item>
	///             <item>websocket客户端新增连接服务器成功的事件，方便实现类似订阅的功能。</item>
	///             <item>Websocket服务器添加心跳检测功能，将会定期（可以自定义）发送心跳包给客户端，在检测客户端是否在线。</item>
	///             <item>文件的服务器和客户端开放文件缓存大小的属性，默认100K，越大的话，性能越高，越占内存。</item>
	///             <item>Modbus协议功能调整，Write(string,short)和Write(string,ushort)功能码调整为06，如果需要0x10功能码，使用Write(string,short[])和Write(string,ushort[])</item>
	///             <item>新增汇川PLC的通讯类，基于modbus协议，但是实现了地址的自动解析，输入D100即可自动转为modbus的地址，包含AM系列，H3U系列，H5U系列等</item>
	///             <item>在示例文档中，新增大量的代码说明，完善注释，如果有任何的问题，优先参考api文档。</item>
	///             <item>官网新增一个来自上海亦仕智能科技有限公司 MES DEMO： http://111.229.255.209 账号SF  密码 123 </item>
	///         </list>
	///     </revision>
	///     <revision date="2020-3-3" version="9.0.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>修复汇川PLC的地址示例文档写错的bug。</item>
	///             <item>IReadWriteNet标准化字符串读写操作，新增定制编码的字符串读写。netDeviceBase移除之前的writeunicode的方法。这点如果有使用，谨慎更新。</item>
	///             <item>串口基类和UDP基类的数据交互方法新增日志记录，对发送的数据和接收的数据写入debug等级的日志。</item>
	///             <item>数据服务器（主要是三菱虚拟plc，西门子虚拟plc，modbus服务器等）实现IReadWriteNet接口。</item>
	///             <item>关于ab-plc，新增MicroCip协议，适用于Micro800系列读写操作。</item>
	///             <item>关于序号生成器类SoftIncrement，重置最大值的方法名称更新，添加了重置当前值，重置初始值，支持反向序列，跳跃序列的功能，详细参考api文档。</item>
	///             <item>文件的服务器类，新增一些日志记录，关于文件何时被读取，何时读取结束的日志信息，等级为debug。</item>
	///             <item>NuGet组件更新，json组件更新到12.0.3版本，IO.port更新到4.7.0版本。单元测试框架更新。</item>
	///             <item>Demo的redis示例，支持不同的db块选择，当你选择数据后自动切换，键值类数据增加格式化显示。</item>
	///             <item>NetworkBase: 网络基类的连接服务器改为如果连接立即失败(500ms内)，将会休眠100ms后，立即再尝试一次，提高连接的成功率。影响范围为所有客户端类。</item>
	///             <item>三菱二进制MC协议：地址里面新增标签访问，缓冲存储器访问，扩展的地址访问的方式，目前开放二进制的mc协议，欢迎测试，顺利的话，完善写入和ascii格式的。</item>
	///             <item>大量的代码注释添加，主流的常用的代码添加中英文注释，后续逐步全都改为中英文，方便国外客户阅读。</item>
	///             <item>240元的普通vip群的激活码时间调整，改为20年，中间软件重启一次，就又是20年，感谢大家的理解和支持。</item>
	///             <item>http://www.hslcommunication.cn/MesDemo 官网的地址以后作为优秀的MES产品展示平台，欢迎大家关注。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-3-15" version="9.1.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>MQTT: 服务器增加定时检测客户端在线情况，超过设置的时间不活跃，强制下线，开放OnlineSession属性，获取在线客户端，查看ip，端口，在线时间等信息。</item>
	///             <item>WebSocket: 服务器开放OnlineSession属性，获取在线客户端，查看ip，端口，在线时间等信息。</item>
	///             <item>Language: 组件的语言系统修复设置英文后，无法切换回中文的bug。</item>
	///             <item>SoftBasic: 添加SpliceByteArray(params byte[][] bytes)方法，用来将任意个byte[]进行拼接成一个byte[]。</item>
	///             <item>SoftBasic: 添加BoolOnByteIndex方法，用来获取byte数据的指定位的bool值。</item>
	///             <item>Panasonic: 松下的mc地址和串口地址统一表示方式：R101=R10.1=[10*16+1]，R10.F=R10.15(这两种表示方式都可以)</item>
	///             <item>发布基于HSL扩展组件HslCppExtension，将写入的重载方法名按照类型重写一遍，方便C++调用。</item>
	///             <item>VC++的demo示例，新增写入数据的例子，基于扩展组件HslCppExtension实现，详细参照demo源代码。</item>
	///             <item>SoftBasic: 针对byte数组的切割，选择头，尾，中间，移除头，尾的方法全部更改成泛型版本，方法名字已经变更，如果有调用，谨慎更新。</item>
	///             <item>FanucInterfaceNet: 新增读取fanuc机器人的通讯类，支持读写任意地址数据的功能，详细参考api文档，写入操作谨慎测试。</item>
	///             <item>FanucRobotServer: 新增fanuc机器人的虚拟服务器，方便进行测试，初始数据来自真实机器人，支持D,I,Q,AI,AQ,M数据区。</item>
	///             <item>Fanuc: 目前测试通过的型号为R-30iB mate plus，其他型号暂时不清楚支持范围。</item>
	///             <item>代码注释优化，api文档大量的更新，添加一些示例代码，包含如果检测状态，长短连接，使用前请仔细阅读下面的信息：http://api.hslcommunication.cn </item>
	///             <item>http://www.hslcommunication.cn/MesDemo 官网的地址以后作为优秀的MES产品展示平台，欢迎大家关注。</item>
	///             <item>三年磨一剑，直插工业互联网的心脏。软件开发之艰辛，如人饮水冷暖自知。感谢所有支持的朋友。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-3-22" version="9.1.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>feat(SAM): 身份证阅读器修复在某些状态下接受数据不完整的bug，将校验数据的完整性。</item>
	///             <item>feat(ab-plc): 虚拟服务器的地址支持小数点的形式，支持单个的bool读写，支持string的读写操作，和客户端的体验一致。</item>
	///             <item>feat(softbasic): 方法针对数组切割的方法，增加扩展方法支持，byte[] a; byte[] b= a.RemoveBegin(2);意思就是移除最前面的2个元素。</item>
	///             <item>feat(softbasic): Hex字符串和byte[]的转化也支持扩展方法。byte[] a.ToHexString()。</item>
	///             <item>feat(melsec): 三菱的a-1e协议之前的，x,y地址采用8进制，先修改为自定义，如果要八进制，地址前面加0，例如X017，如果不加就是十六进制，例如X17，默认十六进制，升级需注意。</item>
	///             <item>feat(melsec): 三菱的a-1e协议增加了F报警继电器，B链接继电器，W链接寄存器，定时器和计数器的线圈，触点，当前值的读取，地址参见api文档说明。</item>
	///             <item>feat(melsec): 添加a-1e协议的ASCII版本，地址格式和二进制版本是一致的，支持的地址类型也是一致的，还未仔细测试，欢迎老铁们测试。</item>
	///             <item>feat(melsec): 三菱的mc虚拟服务器支持二进制和ascii，实例化的时候选择，支持和HSL组件自身的通讯。</item>
	///             <item>lsis: cnet和fenet地址的解析bug修复，感谢埃及朋友的支持。</item>
	///             <item>代码注释优化，使用前请仔细阅读下面的信息：http://api.hslcommunication.cn </item>
	///             <item>http://www.hslcommunication.cn/MesDemo 官网的地址以后作为优秀的MES产品展示平台，欢迎大家关注。</item>
	///             <item>HSL的目标是打造成工业互联网的利器，工业大数据的基础，打造边缘计算平台。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-3-29" version="9.1.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>ModbusAscii: 修复和rtu指令转换时的bug，之前会发生读写失败，目前已经在台达PLC上测试通过。</item>
	///             <item>MelsecA1EAscii：修复三菱的A1E协议的ascii格式类写入单个bool异常的bug。</item>
	///             <item>NetworkUdpServerBase：新增基于UDP协议的服务器基类，后台线程循环接收数据实现。</item>
	///             <item>CipServer: 虚拟的ab-plc服务器新增字符串数组对象的读写操作，demo相关的完善。</item>
	///             <item>HyundaiUdpNet: 新增现代机器人的姿态跟踪网络通讯类，</item>
	///             <item>代码注释优化，使用前请仔细阅读下面的信息：http://api.hslcommunication.cn </item>
	///             <item>http://www.hslcommunication.cn/MesDemo 官网的地址以后作为优秀的MES产品展示平台，欢迎大家关注。</item>
	///             <item>HSL的目标是打造成工业互联网的利器，工业大数据的基础，打造边缘计算平台。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-4-6" version="9.1.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>HslExtension: 完善一些转化的api，方便数组和字符串转化，完善对象转JSON字符串。</item>
	///             <item>LogNet：消息格式化文本的消息等级追随HSL的语言设定，如果是中文，就显示调试，信息，警告，错误，致命。</item>
	///             <item>Redis: 修复ExpireKey，生存时间参数丢失的bug，完善了说明文档。</item>
	///             <item>OmronCip: 欧姆龙的CIP协议的类库，修复数组读取的bug，修复字符串写入bug，字符串写入还需要测试。</item>
	///             <item>Toledo：新增托利多电子秤的串口类及网口服务器类，方便接收标准的数据流，等待测试。</item>
	///             <item>Java：增加了单元测试的内容，对一些已经完成的类添加单元测试。</item>
	///             <item>Python：实现了python版本的HslCommunication程序，基于pyqt实现，初步添加了一些PLC的调试界面。</item>
	///             <item>代码注释优化，使用前请仔细阅读下面的信息：http://api.hslcommunication.cn </item>
	///             <item>http://www.hslcommunication.cn/MesDemo 官网的地址以后作为优秀的MES产品展示平台，欢迎大家关注。</item>
	///             <item>HSL的目标是打造成工业互联网的利器，工业大数据的基础，打造边缘计算平台。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-4-20" version="9.1.4" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>ServerBase: 服务器对象基类完善客户端下线逻辑，精简相关的代码。</item>
	///             <item>LogNet：设备网络通讯类及串口类在数据收发的时候增加日志的记录，可以设置PLC类的LogNet属性抓取相关的报文信息。</item>
	///             <item>ModbusServer: Modbus服务器同时支持TCP,RTU,ASCII，其中RTU和ASCII共用一个串口，根据报文头是否为冒号区分。</item>
	///             <item>ModbusAscii: 修复通讯的bug，已通过单元测试，支持和ModbusServer完美通讯，欢迎网友继续测试。</item>
	///             <item>MelsecMcNet：三菱MC协议的数据地址新增对SB，SW，特殊链接继电器，寄存器的支持。</item>
	///             <item>SiemensServer: 西门子S7虚拟服务器的DB块支持DB1.X，DB2.X，DB3.X，3以上的db块都是使用同一个的DB块。</item>
	///             <item>HttpServer：自定义轻量级的WebApi服务器支持反射对象的方法名，简化定义API时定义大量的if...else...。</item>
	///             <item>UdpNet：添加ConnectionId属性，使用的<seealso cref="M:HslCommunication.BasicFramework.SoftBasic.GetUniqueStringByGuidAndRandom" />方法获取信息。</item>
	///             <item>MelsecMcRNet：添加三菱R系列的MC协议二进制的实现，和标准的有一点区别，地址支持也不一样，欢迎测试Demo。</item>
	///             <item>OmronCip：欧姆龙的读写数组已经测试通过，修改了读写字符串的逻辑实现，等待测试。</item>
	///             <item>代码注释优化，使用前请仔细阅读下面的信息：http://api.hslcommunication.cn </item>
	///             <item>http://www.hslcommunication.cn/MesDemo 官网的地址以后作为优秀的MES产品展示平台，欢迎大家关注。</item>
	///             <item>HSL的目标是打造成工业互联网的利器，工业大数据的基础，打造边缘计算平台。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-4-28" version="9.2.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>HttpServer: 当客户端发起request请求的时候，在日志记录的时候记录当前的请求的方式，GET,POST,OPTION等等。</item>
	///             <item>MQTT: mqtt的消息等级追加一个新的等级，为OnlyTransfer等级，用来表示只发送服务器，不触发发布操作。</item>
	///             <item>MqttServer: 配合Qos等级为OnlyTransfer时，进行相关的适配操作，并触发消息接收的事件。</item>
	///             <item>MqttSyncClient: 新增MQTT的同步访问的客户端，协议头标记为HUSL，向HSL的mqtt服务器进行数据请求并等待反馈。尚未添加心跳程序。</item>
	///             <item>MqttServer: 适配同步客户端实现功能，当客户端为同步客户端的时候，调试心跳验证。</item>
	///             <item>至此，HSL的MQTT协议已经是兼容几大网络功能了，在线客户端管理，消息发布订阅，消息普通收发，同步网络访问。</item>
	///             <item>IByteTransform接口属性新增IsStringReverseByteWord，相当于从ReverseByWord挪过来了，默认为false，如果为true，在解析字符串的时候将两两字节颠倒。</item>
	///             <item>Omron: 欧姆的fins-tcp及fins-udp及hostlink的IByteTransform接口IsStringReverseByteWord调整为true默认颠倒。</item>
	///             <item>SerialBase: 串口基类的打开串口方法调整返回类型OperateResult，在串口数据读取之前增加打开串口的Open方法，串口类也只需要一直读就可以了。</item>
	///             <item>NetworkDoubleBase, SerialDeviceBase, NetworkUdpDeviceBase及相关的继承类，对所有的泛型进行了擦除，一律采用接口实现，之后将统一java,python代码。</item>
	///             <item>FreedomTcp,FreedomUdp,FreeSerial: 添加基于自由协议的tcp，udp，串口协议，可以自由配置IByteTransform接口，可用来读取一些不常见的协议。</item>
	///             <item>Omron-cip: 读写字符串仍然没有测试通过，请暂时不要调用。</item>
	///             <item>SiemensS7: 单次读取之前是按照200字节进行拆分的，现在根据s7协议返回的报文来自动调整，1200系列是220字节，1500系列是920字节，其他等待测试。</item>
	///             <item>官网的备案失效了，重新备案需要点时间，请访问 http://118.24.36.220 然后去顶部的菜单找相应的入口。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///             <item>HSL的目标是打造成工业互联网的利器，工业大数据的基础，打造边缘计算平台。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-5-6" version="9.2.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Toledo: 托利多电子秤的字节触发的时候，传递出来携带原始的字节数组，方便自行处理，Demo界面优化，显示信息更加完善。</item>
	///             <item>Lsis: Lsis的PLC通信类修复一些bug，感谢埃及朋友的提供的技术支持。</item>
	///             <item>MqttSyncClient: 新增ReadString方法，以字符串的形式来和服务器交互，默认编码UTF8，当然也可以自己指定编码，本质还是读取字节数据。</item>
	///             <item>WebsocketClient: websocket的客户端类，重新设计异常重连，网络异常时触发 OnNetworkError 事件，用户应该捕获事件，然后在事件里重连服务器，直到成功为止。</item>
	///             <item>MqttClient: Mqtt客户端类，重新设计异常重连，网络异常时触发 OnNetworkError 事件，用户应该捕获事件，然后在事件里重连服务器，直到成功为止。</item>
	///             <item>MqttSyncClient: 支持读取数据的进度回调功能，支持三种进度报告，数据上传到服务器的进度报告，服务器处理进度报告，数据返回到客户端的进度报告。</item>
	///             <item>PanasonicMewtocol: 修复注释错误，L区的数据也可以进行L100F，L2.3访问。</item>
	///             <item>DLT645: 初步添加电力规约协议的串口实现，目前只实现了读取数据，还未测试，等待后续的测试完善。</item>
	///             <item>Omron-cip: 读写字符串仍然没有测试通过，请暂时不要调用。</item>
	///             <item>官网的备案失效了，重新备案需要点时间，请访问 http://118.24.36.220 然后去顶部的菜单找相应的入口。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///             <item>HSL的目标是打造成工业互联网的利器，工业大数据的基础，打造边缘计算平台。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-5-11" version="9.2.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>MqttClient: 上个版本开放的网络错误事件，如果不进行事件绑定，增加默认实现，每隔10秒去连接服务器，直到成功为止。</item>
	///             <item>WebsocketClient: 上个版本开放的网络错误事件，如果不进行事件绑定，增加默认实现，每隔10秒去连接服务器，直到成功为止。</item>
	///             <item>DLT645: 电力规约协议完善，等待后续的测试完善。</item>
	///             <item>SerialBase: ReadBase提供一个重载的方法，ReadBase( byte[] send, bool sendOnly )支持单向发送，不接收数据返回。</item>
	///             <item>SoftBasic: HexStringToBytes算法优化，性能提升，移除了转大写字母的步骤。</item>
	///             <item>SiemensS7: 开放获取 pdu 数据长度属性，属性名称：PDULength</item>
	///             <item>HslExtension: 增加IncreaseBy方法，但是测试发现不适用byte类型。</item>
	///             <item>Omron-cip: 读写字符串仍然没有测试通过，请暂时不要调用。</item>
	///             <item>官网的备案失效了，重新备案需要点时间，请访问 http://118.24.36.220 然后去顶部的菜单找相应的入口。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///             <item>HSL的目标是打造成工业互联网的利器，工业大数据的基础，打造边缘计算平台。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-5-21" version="9.2.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>IReadWriteNet接口新增一个属性，ConnectionId，用来表示设备的唯一ID。</item>
	///             <item>ModbusTcpServer: Modbus的虚拟服务器支持0x16功能码，支持掩码写入操作，适用Tcp,Rtu,Ascii。</item>
	///             <item>Modbus客户端(tcp+rtu+ascii+rtuovertcp) 新增掩码写入方法，WriteMask，bool写入时，假如Write("100.1", true)就使用掩码写入寄存器100的第1位为真。</item>
	///             <item>RedisClient: redis的客户端新增Ping方法，DBSize方法获取key数量，FlushDB方法清除数据库所有key。</item>
	///             <item>DTUServer: 新增一个DTU服务器，可以用来实现对plc的反向连接操作，根据设备的唯一号来识别。</item>
	///             <item>Omron-cip: 读写字符串不成功的bug修复，已经测试通过。</item>
	///             <item>WebsocketClient: 实例化时新增url的额外参数传递，("127.0.0.1", 1883, "/A/B?C=123456")，也可以使用"ws://127.0.0.1:1883/A/B?C=123456"。</item>
	///             <item>WebsocketClient: 修复未连接服务器的时候，调用关闭方法将会引发发送异常的bug。</item>
	///             <item>MqttServer: 修复NET35版本不支持同步访问的bug，新增一个客户端断开连接的事件，OnClientDisConnected事件。</item>
	///             <item>VibrationSensor: 新增一个震动传感器的类，型号为苏州捷杰震动传感器VB31，支持获取速度，加速度，位移，温度信息。</item>
	///             <item>官网的备案失效了，重新备案需要点时间，请访问 http://118.24.36.220 然后去顶部的菜单找相应的入口。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///             <item>HSL的目标是打造成工业互联网的利器，工业大数据的基础，打造边缘计算平台。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-5-29" version="9.2.4" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Mewtocol: 松下的串口协议修复LD寄存器无法访问的bug，输入LD100，如果只输入L100，就是线圈。</item>
	///             <item>Modbus: 修复写入寄存器指定位bool失败的bug，写入true的掩码改为 FF FE，00 01</item>
	///             <item>Modbus：在ModbusRtuOverTcp里填写掩码写入的api方法。</item>
	///             <item>ab-plc：CIP协议解析标签地址的编码从ASCII编码修改为UTF-8编码，支持中文的标签名访问。</item>
	///             <item>omron-plc：CIP协议解析标签地址的编码从ASCII编码修改为UTF-8编码，支持中文的标签名访问。</item>
	///             <item>Websocket: 连接的请求标头修改为GET ws://127.0.0.1:8800/ HTTP/1.1  就是带IP地址及端口信息</item>
	///             <item>Redis：Redis的客户端添加对集合和有序集合操作的相关API方法，基本支持了所有需要的操作信息，单元测试通过。</item>
	///             <item>Demo: 所有DEMO写入数据操作，新增Hex写入，输入1A 1B等十六进制数据，然后底层调用Write(string, byte[])方法。</item>
	///             <item>Demo：Redis的功能菜单新增一个测试界面，用来同步两个不同的redis的数据，也可以同一个redis不同的db块数据。</item>
	///             <item>官网的备案失效了，重新备案需要点时间，请访问 http://118.24.36.220 然后去顶部的菜单找相应的入口。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///             <item>HSL的目标是打造成工业互联网的利器，工业大数据的基础，打造边缘计算平台。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-6-10" version="9.2.5" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>CipServer: Cip的虚拟服务器的数据节点编码修改为UTF8编码，增加了一些可读性比较强的增加节点的api，支持赋值初始化数据。</item>
	///             <item>Demo: Kuka机器人的连接问题，请参考下面地址：http://blog.davidrobot.com/2019/03/hsl_for_kuka.html?tdsourcetag=s_pctim_aiomsg </item>
	///             <item>Redis: 增加读取TTL的api方法，方便的获取剩余的生存时间。</item>
	///             <item>HttpServer: 修复Response为空时进行AppendHeader时发生的bug，进行二次校验。</item>
	///             <item>VibrationSensorClient: 修复deme站号设置失效的bug，站号根据接收的数据动态调整，增加检测长时间未接收传感器数据，就选择重连的功能。</item>
	///             <item>官网的备案失效了，重新备案需要点时间，请访问 http://118.24.36.220 然后去顶部的菜单找相应的入口。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///             <item>HSL的目标是打造成工业互联网的利器，工业大数据的基础，打造边缘计算平台。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-6-28" version="9.2.6" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>NetworkBase: 同步网络通信的超时检查不再开新的线程检查，使用socket自带的ReceiveTimeout来检查。</item>
	///             <item>NetworkBase: 发送数据时，增加对发送数据的空检查，如果为空，就认为成功。</item>
	///             <item>RedisClient: 新增修改密码的API接口，可以进行对redis的密码重置操作。</item>
	///             <item>MqttServer: 当同步客户端 MqttSyncClient连接上来时，不进行触发上下线事件。</item>
	///             <item>MqttServer：原先支持获取所有的在线客户端，现在新增获取异步客户端列表，获取同步客户端列表。</item>
	///             <item>MqttSubscribeMessage: 类型拼写错误修复，如果使用这个类，请谨慎升级。</item>
	///             <item>Keyence: 基恩士的MC协议，支持CC，TC的数据类型读取。</item>
	///             <item>FanucSeries0i: 新增一个fanuc机床的数据通讯类，支持读取一些简单的数据，目前在Series0i-F上测试通过。</item>
	///             <item>Cip: 修复ab-plc的标签地址解析为UTF-8编码，但是长度确实字符串的bug，现在支持中文编码。</item>
	///             <item>其他的注释优化</item>
	///             <item>官网的备案失效了，重新备案需要点时间，请访问 http://118.24.36.220 然后去顶部的菜单找相应的入口。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///             <item>HSL的目标是打造成工业互联网的利器，工业大数据的基础，打造边缘计算平台。企业终身授权费：8000元(不含税)。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-7-8" version="9.2.7" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>MqttServer: 修复MQTT服务器开启时，当用其他的mqtt客户端订阅时，会发生异常的bug，原因在于订阅质量没有回传。</item>
	///             <item>WebsocketServer: websocket的服务器端新增一个客户端下线的事件，无论是正常关闭还是异常关闭，都会触发事件。</item>
	///             <item>MqttClient: Mqtt的客户端新增一个连接成功的事件OnClientConnected，重连成功后也会触发。在该事件的订阅topic会在网络恢复后重新订阅。</item>
	///             <item>NetworkDoubleBase: 当校验指令头失败的时候，返回的错误信息里追加，收发的报文，方便查找问题。</item>
	///             <item>MelsecA1EAsciiNet: 修复读取bool时，长度为奇数时，会出现交替失败的bug，原因出自数据粘包。</item>
	///             <item>WebsocketClient: 添加一个IsClosed属性，修复服务器强制断线导致客户端无限重连的bug。</item>
	///             <item>OmronConnectedCipNet: 添加一个基于连接的CIP的读写类，等待测试。</item>
	///             <item>其他的注释优化</item>
	///             <item>官网的备案失效了，重新备案需要点时间，请访问 http://118.24.36.220 然后去顶部的菜单找相应的入口。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///             <item>HSL的目标是打造成工业互联网的利器，工业大数据的基础，打造边缘计算平台。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-7-20" version="9.2.8" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>KeyenceNanoSerial: 基恩士的串口协议重新实现，实现IReadWrite接口，增加了单元测试。支持的地址需要查阅API文档信息。</item>
	///             <item>OmronHostLinkCMode: 支持了欧姆龙的HOSTLINK协议的Cmode模式的实现，初步单元测试通过，等待测试。</item>
	///             <item>MC协议：三菱MC协议的ZR区的地址进制从16进制改为10进制。</item>
	///             <item>NetworkDoubleBase: 添加一个PING的方法IpAddressPing( ), 对设备当前的IP地址进行PING操作。</item>
	///             <item>NetworkUdpBase: 添加一个PING的方法IpAddressPing( ), 对设备当前的IP地址进行PING操作。</item>
	///             <item>yamaha: 添加一个雅马哈机器人协议的实现，初步实现了几个api，等待测试，测试通过继续完善。</item>
	///             <item>DEMO: 主界面增加一个全国使用情况的分布图，统计DEMO的使用次数实现。</item>
	///             <item>官网的备案失效了，重新备案需要点时间，请访问 http://118.24.36.220 然后去顶部的菜单找相应的入口。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///             <item>HSL的目标是打造成工业互联网的利器，工业大数据的基础，打造边缘计算平台。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-8-3" version="9.3.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Networkbase：核心网络底层的错误码调整，当读写操作因为网络问题失败时，返回错误码为负数-1，如果连续读写失败，就一直递减。</item>
	///             <item>OmronConnectedCipNet: 地址解析修改为全部上29 00 报文。</item>
	///             <item>FileNet: 两种文件服务器支持删除多个文件和删除文件夹的所有文件功能，客户端同步适配，初步测试通过。</item>
	///             <item>NetSimplifyClient: 新增一个构造方法，可以传入IPAddress类型的ip地址。</item>
	///             <item>MqttSyncClient: 新增一个构造方法，可以传入IPAddress类型的ip地址。</item>
	///             <item>MqttClient: 修复一个连接反馈信号，解析判断服务器状态错误的bug，该bug导致MqttClient连接不是中国移动的OneNet物联网框架。</item>
	///             <item>FFT: 傅立叶变换FFTValue方法添加一个可选参数，是否二次开放，波形中的毛刺频段会更加明显。</item>
	///             <item>HttpServer: webapi的服务器完善注释，添加一个端口号的属性，获取当前配置端口号信息。</item>
	///             <item>Active: 当前库激活失效的时候，返回的错误消息，携带当前的通信对象的实例化个数，方便查找授权失败的原因。</item>
	///             <item>Abb机器人：abb机器人支持读取程序执行状态，任务列表功能，伺服状态，机器人位置数据。</item>
	///             <item>ABB虚拟机器人：新增一个abb机器人的虚拟webapi的服务器，可以用来测试和ABB客户端的通信。</item>
	///             <item>Demo: 数据转换的界面，新增一个显示指定的文件的二进制的内容的功能。当demo激活成功时，不显示时间及授权信息。</item>
	///             <item>新增一篇全新的博文，介绍基于HSL的大一统网络架构实现，满足发布订阅，一对多通信，webapi等：https://www.cnblogs.com/dathlin/p/13416030.html。</item>
	///             <item>官网备案成功了，地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///             <item>HSL的目标是打造成工业互联网的利器，工业大数据的基础，打造边缘计算平台。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-8-28" version="9.3.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Beckhoff: 倍福PLC新增读取设备信息和设备状态的api接口。在demo界面添加测试按钮，状态码检查优化，错误时返回报文信息。</item>
	///             <item>FanucSeries0i: 修复fanuc机床的读取宏变量解析double数据时，为0的时候解析异常的bug。</item>
	///             <item>ABBWebApiServer：ABB机器人的虚拟服务器支持用户名和密码设置，在客户端请求数据的时间，支持账户验证。</item>
	///             <item>Demoserver: 优化根据IP地址获取物理地址的方法，获取不到或是奇怪字符将切换线路重新获取。</item>
	///             <item>KukaTcpNet: 新增KukaTcp通讯类，支持多变量写入的api，在demo界面增加启动，复位，停止程序的操作。</item>
	///             <item>.Net Framwork 2.0 支持2.0的框架的dll发布，通过nuget安装即可。</item>
	///             <item>SimpleHybirdLock: 简单混合锁添加一个当前进入锁的次数的静态属性，可以查看当前共有多少锁，等待多少锁。</item>
	///             <item>NetworkDeviceBase: 核心交互方便增加错误捕获，异常释放锁，再throw, YamahaRCX类完善异步方法</item>
	///             <item>NetworkBase: 增加一个线程检查超时的次数统计功能。</item>
	///             <item>InovanceH3U: 修复汇川的3U的PLC地址类型为SM,SD时解析异常的bug。</item>
	///             <item>Demo: HslCommunication Test Demo支持PLC及一些连接对象的参数保存功能，使用英文冒号可以分类管理。</item>
	///             <item>WebSocketSession: 新增url属性，如果客户端请求包含url信息，例如：ws://127.0.0.1:1883/A/B?C=123, 那么url就是这个值。</item>
	///             <item>Demo: 测试的DEMO程序，支持连接参数存储，不用再每次打开程序重新输入IP地址，端口，站号等等信息，可以存储起来，还支持分类存储。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///             <item>HSL的目标是打造成工业互联网的利器，工业大数据的基础，打造边缘计算平台。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-9-27" version="9.3.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>KeyenceNanoSerial: 修复读写R寄存器时，提示地址格式异常的BUG，已经测试通过。</item>
	///             <item>MelsecMcUdpServer: 新增三菱MC协议的UDP虚拟PLC，支持数据读写，支持二进制和ASCII格式。</item>
	///             <item>OmronFinsUdpServer: 新增欧姆龙Fins协议的UDP的虚拟PLC，支持数据读写操作。</item>
	///             <item>MqttServer: 修复MQTT服务器在客户端发送批量订阅的时候，服务器会触发BUG的问题。</item>
	///             <item>ConnectPool&lt;TConnector&gt;类代码注释优化，新增连接次数峰值属性。</item>
	///             <item>RedisSubscribe: 订阅服务器重新设计，订阅实现事件触发，支持手动订阅，取消订阅操作。</item>
	///             <item>RedisClient: 支持了订阅的操作，当订阅的时候，创建订阅的实例化对象，应该在连接参数设置之后再进行订阅。</item>
	///             <item>RedisClientPool：新增Redis连接池类，默认不限制连接数量，使用起来和普通的RedisClient一样，适合一个项目实例化一个对象。</item>
	///             <item>MqttSyncClientPool: 新增MqttSyncClient的连接池版本类，默认不限制连接数量，用起来和普通的MqttSyncClient一样。</item>
	///             <item>LogNetFileSize: 根据文件大小的日志类，实例化时支持设置允许存在的文件上限，如果设置为10，只保留最新的10个日志文件。</item>
	///             <item>LogNetDateTime: 根据日期的日志类，实例化时支持设置允许存在的文件上限，如果设置为按天存储，上限为10，就是保留10天的日志。</item>
	///             <item>AllenBradleySLCNet: 新增AB PLC的数据访问类，适合比较老的AB PLC，测试通过的是1747系列。地址格式为A9:0</item>
	///             <item>AllenBradleyNet: 读写bool值的时候，不带下标访问单bool数据，如果需要访问bool数组，就需要带下标访问，例如：A[0]。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-10-23" version="9.5.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>AllenBradleyNet: ReadBool方法默认读取单bool类型变量，如果要读取int类型的bool数组，采用"i="开头表示，例如"i=A[10]"</item>
	///             <item>NetworkDataServerBase: 新增一个属性ActiveTimeSpan，可以设置激活时间间隔，默认24小时，锁优化，其他的继承实现的服务器都进行了设置。</item>
	///             <item>NetworkDeviceBase: Read&lt;T&gt;修改为虚方法，支持继承进行重写，基于特性的类注释完善。</item>
	///             <item>Siemenss7net: ReadString(string address, ushort length)读取字符串时，如果长度为0，就读取西门子格式的字符串。</item>
	///             <item>OperateResult: 扩充泛型方法，Check, Convert, Then，实现了结果链，简化代码。参考：https://www.cnblogs.com/dathlin/p/13863115.html </item>
	///             <item>FanucSeries0i: 修复数控机床在读取0i-mf状态时导致长度不够的bug。</item>
	///             <item>IReadWriteNet: 新增wait方法接口，用于等待一些信号到达指定的值，支持扫描频率设置，超时设置。例如 Wait("M100.0", true, 500, 10000)等待这个信号为true为止。</item>
	///             <item>MqttServer: 支持调用ReportOperateResult返回错误信息及错误码给客户端，MqttSyncClient会自动识别报文，然后IsSuccess自动适应，网络不会断开。</item>
	///             <item>MqttSyncClient: 支持设置接收超时时间，默认是60秒，之前是5秒，而且不能更改。</item>
	///             <item>MqttServer: 支持注册远程RPC的API接口，自动解析json参数，自动调用已经注册的接口自动返回是否成功，MqttSyncClient也支持遍历服务器的接口列表。详细：https://www.cnblogs.com/dathlin/p/13864866.html </item>
	///             <item>SiemensS7Net: 通信类实现ReadBool("M100", 10); 批量读bool方法，通过读Byte间接实现。</item>
	///             <item>OmronHostLinkCModeOverTcp: 新增欧姆龙的通讯类，Cmode模式的以太网透传实现。</item>
	///             <item>PLC: 所以的PLC实现了HslMqttApi特性支持，从而在MqttServer里可以直接注册，然后对外开放读写接口操作。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-11-2" version="9.5.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>MQTTServer: 服务器端在注册M-RPC远程调用接口服务的时候，如果你的接口实现了参数默认值，那么就提取到示例参数里。</item>
	///             <item>KeyenceSR2000SeriesTcp: 新增基恩士的SR2000系列的扫码器的驱动，目前是TCP版本，支持读码，读取记录，停止复位等，支持自定义的命令。</item>
	///             <item>XGKFastEnet: 新增Lsis的XGK系列的FastEnet实现。</item>
	///             <item>XGKCnet: 新增Lsis的XGK的cnet的实现。</item>
	///             <item>Demo: Demo的TCP调试的服务器端优化，错误获取优化，发送数据失败的问题修复。</item>
	///             <item>NetworkBase: 底层异步的数据接收的超时优化，优化超时线程池实现，更加节省线程调度。</item>
	///             <item>MqttSyncClient: 客户端支持读取MQTT服务器的驻留主题列表，读取该主题的相关的数据信息。详细见demo。</item>
	///             <item>MqttServer: 修改ClientVerification事件，增加会话句柄传递，支持动态修改client id，支持设置当前客户端禁止发布任何数据。</item>
	///             <item>MqttServer上的MRPC的权限控制仅对商业授权用户开放，MqttSyncClientPool连接池以及RedisClientPool连接池仅对商业授权用户开放。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-11-11" version="9.5.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>XGKFastEnet修复了一点小问题，感谢埃及朋友提供的技术支持。</item>
	///             <item>KeyenceSR2000Serial: 新增基恩士的SR2000系列的扫码器的串口版驱动，支持读码，读取记录，停止复位等，支持自定义的命令。</item>
	///             <item>IKeyenceSR2000Series: 新增基恩士的SR2000系列的扫码器接口，优化读码功能，如果读码失败，则发送LOFF命令。</item>
	///             <item>LogStatistics: 新增统计次数的辅助类，可以实现关于时间的一些统计信息，比如统计每小时，每天，每周，每月等的登录量，使用量信息。</item>
	///             <item>LogValueLimit: 新增数据极值类，用于统计一个数的开始值，结束值，最大值，最小值，平均值，然后可以按小时，天，周，月等统计。</item>
	///             <item>OmronHostLink: 修复解析错误码时，如果错误码不为数字的时候会导致奔溃的bug，错误提供内容更加详细。</item>
	///             <item>ILogNet: 日志记录的方法实现MqttApi特性，可以在MQTTServe里面注册为RPC服务，从而实现远程调用日志写入的方法。</item>
	///             <item>ILogNet: 日志类方法新增属性LogStatistics，只要实例化就可以统计当前的日志记录情况，可以每分钟，每小时，每天，每周，每月，每季度，每年。</item>
	///             <item>MqttClient: 新增一个订阅的api接口，支持直接传递MqttSubscribeMessage对象，可以指定消息质量。</item>
	///             <item>XinJEXCSerial: 新增信捷的XC系列的串口通讯类，底层是modbus-rtu，地址做了封装，按照信捷的地址输入即可，比如X1,Y7,M1000,D100,F100,E100。</item>
	///             <item>MelsecFxSerial: 三菱编程口类新增 IsNewVersion 属性，如果为false，就是老版本的协议，修复T,C线圈读写的地址不对BUG。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-12-2" version="9.5.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>ModbusRtuOverTcp: 更改继承，直接从NetworkDeviceBase进行继承，通过单元测试</item>
	///             <item>YokogawaLinkTcp: 新增横河PLC的二进制通讯类，支持X,Y,I,E,M,T,C,L,D,B,F,R,V,Z,W,TN,CN读写，部分高级API商业授权用户才能使用，例如读取PLC信息。</item>
	///             <item>YokogawaLinkServer: 新增横河PLC的二进制格式的虚拟PLC，模拟的真实的PLC的通信机制，实现了读写长度的限制，以及错误信号的返回。</item>
	///             <item>Networkdoublebase: ReadFromCoreServer( byte[] send, bool hasResponseData ) 新增是否等待数据返回的属性，可以用于某些不需要数据返回的命令。</item>
	///             <item>Networkbase： 修复异步接收数据时，某些情况下长度为0导致连接关闭的bug。</item>
	///             <item>FetchWriteServer: 新增西门子fetch/Write协议的虚拟PLC，支持虚拟数据的读写，通信。</item>
	///             <item>MelsecFxSerialOverTcp: 修改继承体系，从NetworkDeviceBase继承，和MelsecFxSerial的IsStringReverseByteWord调整为true;</item>
	///             <item>文件引擎服务器修复路径大小写导致的bug问题，文件客户端支持检查文件是否存在的方法，检查文件是否存在。</item>
	///             <item>MqttServer: 远程调用的MRPC的参数支持自定义类型，通过JSON转换，将字符串转换为实体类。还有其他的优化。</item>
	///             <item>DeltaDvpSerial, DeltaDvpSerialAscii, DeltaDvpTcpNet: 添加台达的通信类，输入台达的地址即可，会自动转换实际的modbus地址。</item>
	///             <item>所有的虚拟PLC的服务器均调整为商业授权用户专享，还有一些高级的API，具体看api注释是否带有[商业授权]字样，基本的数据读写功能将一直对个人用户开放。</item>
	///             <item>Demo: 数据读写示例的界面，写入现在支持批量写入，数据写[1,2,3]，然后写入short，就是写入short数组了。</item>
	///             <item>普通VIP的个人使用不再限制100个PLC对象，连续运行时间调整为10年，高级的一些API限制商用，参考注释是否带[商业授权]字样。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2020-12-22" version="9.6.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>YokogawaLink: 异步的方法添加和完善，虚拟PLC侧支持Special:100开头的地址，表示特殊模块寄存器，从而支持各种类型的读写。</item>
	///             <item>LogStatistics, LogValueLimit: 两个数据日志分析类支持获取指定时间段的数据，数据存储文件格式重新设计，为不兼容更新。</item>
	///             <item>LogStatisticsDict, LogValueLimitDict: 新增数据日志分析的词典类，用来统计多个数据的不同时间段的使用情况。</item>
	///             <item>所有的基于tcp的plc，机器人，redis, mqtt, websocket等通讯类的ip地址支持直接输入域名，会自动调用Dns.GetIpAddress来解析。 </item>
	///             <item>MqttRpcApi: MRpc的API特性支持应用在属性上，不需要传递参数，直接获取属性的值，在demo上显示的小图标不一样。PLC的通讯类的基本属性在MRPC公开。</item>
	///             <item>MelsecFxLinks: 支持读取PLC的型号，读写数据的地址支持了站号指定，地址可以写成[s=2;D100]，方便多站号读取。</item>
	///             <item>AllenBradleyNet: 地址支持slot参数，例如：slot=2;AAA ，也可以不携带，这个是可选的。</item>
	///             <item>FatekProgram, FujiSPB, XGBCnet, MelsecA3CNet1, OmronHostLink, OmronHostLinkCMode, PanasonicMewtocol, SiemensPPI，信捷，汇川类及其透传类支持地址携带站号，例如 s=2;D100</item>
	///             <item>FujiSPBServer: 新增富士PLC的虚拟服务器，支持串口和网口，原先的富士PLC存在bug，不能读取，欢迎网友对富士PLC测试。</item>
	///             <item>HttpServer: 删除原先的HttpGet和HttpPost特性，改用MRPC的特性，支持注册webapi服务，使用方式和MRPC类似，demo增加httpsclient可浏览接口，https://www.cnblogs.com/dathlin/p/14170802.html</item>
	///             <item>HttpServer, MqttServer: 服务器端支持接口调用的次数统计，支持客户端查询接口调用情况，demo客户端实现mqttclient,方便服务器管理在线客户端信息。</item>
	///             <item>其他优化改进，如果有网友发现bug，配合作者测试并修复bug，将根据实际情况给与现金红包奖励。</item>
	///             <item>普通VIP的个人使用不再限制100个PLC对象，连续运行时间调整为10年，高级的一些API限制商用，参考注释是否带[商业授权]字样。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-1-12" version="9.6.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Lsis XGK: 修复部分的问题，感谢埃及朋友提供的支持。</item>
	///             <item>FujiSPB: 修复未在net20, net35, net standard项目里添加的bug。</item>
	///             <item>MqttServer和HttpServer: 注册API的方法支持对静态方法的注册，注册时传入类型对象即可。</item>
	///             <item>Modbus: tcp, rtu, ascii, rtu over tcp在读写int,uint,float,double,long,ulong时支持动态指定dataformat，地址示例：format=BADC;100</item>
	///             <item>MqttServer: 扩展MQTT的子协议FILE，支持文件的上传，下载，删除，查看信息，权限控制操作，支持获取上传下载网速监控。</item>
	///             <item>MqttSyncClient: 扩展文件的方法接口，支持上传，下载，删除，遍历文件操作，每个操作都是短连接的，使用的全新的socket对象。</item>
	///             <item>SiemensS7Net: 修复西门子s7协议某些情况数据批量写入失败的bug，原因来自PDU长度信息不对。</item>
	///             <item>DLT645: 修复一些问题，已经测试通过，新增 DLT645OverTcp，感谢 QQ：542023033 提供的技术支持。</item>
	///             <item>FanucInterface: 机器人的解析数据时，当shift_jis编码不存在时，将会引发异常，现在自动替换UTF8</item>
	///             <item>HslCommunication: 所有的异步通信代码优化，优化超时检测机制，现在大大提升了服务器的高并发的能力，异步通信的性能。</item>
	///             <item>AllenBradleyNet及OmronCipNet协议支持 UINT, UDINT, ULING类型的写入，对应的C#的类型是 ushort, uint, ulong</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-1-26" version="9.6.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>OmronConnectedCipNet: 新增欧姆龙的基于连接的CIP实现，测试读写欧姆龙PLC成功，支持数组读写，详细参考API文档，欧姆龙的CIP请使用本类。</item>
	///             <item>AllenBradleyNet: 修复当自动重连时，连接的ID 还是上次的 ID 导致读写失败的bug。</item>
	///             <item>DLT645: 点对点模式下，在读取地址域的时候，新增读取成功后即修改当前的地址域信息，也即是在打开串口后，读取地址域即可通信。</item>
	///             <item>DLT645: 修复有些数据格式不一致导致读取数据不正常的bug，已经测试可以读取功率，能耗，电压电流，电表基本信息，还支持自定义的解析格式。</item>
	///             <item>NetworkAlienClient: DTU客户端增加对连接客户端的注册包的数据校验，修复数据意外的情况导致程序奔溃的bug。</item>
	///             <item>Demo: 在 演示程序里，Modbus的DTU的示例界面，修复 ID 设置时，结果设置到 IP 导致异常的bug。另外增加西门子的DTU演示界面。</item>
	///             <item>LSisServer: 修复同一地址，数据读写不对的bug，和 XGKFastEnet 客户端读写测试通过，包括bool类型地址，字地址</item>
	///             <item>GeSRTPNet: 新增 GE-PLC（通用电气） 的SRTP协议实现的客户端，支持I,Q,M,T,SA,SB,SC,S,G 的位和字节读写，支持 AI,AQ,R 的字读写操作。</item>
	///             <item>GeSRTPServer: 新增 GE 的 SRTP 协议的虚拟PLC，支持和 GeSRTPNet 通信测试。支持类型和客户端支持的一致。</item>
	///             <item>MqttServer: 在启动文件服务功能时，增加对分类路径，以及文件名的合法性进行校验，防止注入特殊字符攻击及意外bug。</item>
	///             <item>MqttSession: 新增一个方法，GetTopics() 用于获取当前的会话对象所订阅的主题的副本数据。</item>
	///             <item>PanasonicMewtocol: 修复 Mewtocol及串口转网口类，在批量读取 bool 数组地址解析不准确的bug。</item>
	///             <item>MelsecCipNet: 新增三菱的CIP协议功能，PLC使用了 QJ71EIP71 模块时就需要使用本类来访问。</item>
	///             <item>SickIcrTcpServer: 修复当关闭服务器的时候，现有的连接没有关闭的bug，没有关闭的话，仍然会接收到来自设备发来的条码信息。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-2-15" version="9.6.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>SickIcrTcpServer: 修复手动连接扫码设备的网络在关闭服务器后仍然会自动重连的bug。</item>
	///             <item>SoftBasic: 删除SpliceTwoByteArray及SpliceByteArray方法，改为泛型支持的SpliceArray方法，支持任意类型拼接，添加了扩展方法支持。</item>
	///             <item>Modbus: 支持 0x16 功能码，用于掩码操作，支持对寄存器地址的位操作，需要设备方支持，该功能仅支持商业授权使用。</item>
	///             <item>Modbus: 读取线圈和输入线圈的长度支持任意，内部按照2000长度自动切割，读取寄存器和输入寄存器按照120自动切割，该功能商业授权特权，普通的VIP用户存在长度限制。</item>
	///             <item>MqttSyncClient: 新增ReadRpc&lt;T&gt;(string topic, string payload )方法，专门用来读取注册的RPC接口的，自动json转换类型。</item>
	///             <item>MqttSyncClientPool: 连接池优化，注释优化，添加了一些缺失的方法。该功能商业授权特权。</item>
	///             <item>RedisClientPool: 连接池优化，注释优化。该功能商业授权特权。</item>
	///             <item>LogNet: 日志部分新增一个 ConsoleOutput 属性，如果设置为 true，那么日志就会在控制台进行输出，等级不一样的日志，文字颜色不一样。</item>
	///             <item>LogNet: 日志部分的记录优化调整，取消了一些底层的重复记录的日志内容，针对 MQTT, Websocket, HTTP 及虚拟PLC相关的日志记录根据信息进行优化。</item>
	///             <item>祝大家新年生意滚滚，身体健康，牛年大吉。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-3-11" version="9.6.4" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>ILogNet: 当ConsoleOutput为true时，修复在空字符串的存储路径时，不进行控制台输出的bug。</item>
	///             <item>SiemensS7Server: 优化握手报文的信息，现在支持sharp7进行数据通信了。</item>
	///             <item>SiemensS7Net: 开放了 ConnectionType 和 LocalTSAP 属性，方便按照自己的需求修改，具体看属性注释。</item>
	///             <item>SiemensS7Net: 西门子的PLC读取支持0x0A和0x06的错误码，当读取DB块不存在时，提示错误消息。</item>
	///             <item>SiemensS7Net: 支持了 WString 类型的读写，使用ReadWString和WriteWString方法，支持中文的读写</item>
	///             <item>SiemensS7Net: 西门子S7协议的地址解析，DB块地址优化，DB1.DBW1 的 DBX,DBB,DBW,DBD都会自动屏蔽。</item>
	///             <item>SiemensS7Net: 在核心的报文交互上，自动忽略只有7字节的TPKT和ISO的报文的情况。</item>
	///             <item>MqttServer, HttpServer:RPC注册的方法，原先只支持一个泛型的结果类 OperateResult&lt;T&gt;， 现在支持任意个泛型的结果类对象。</item>
	///             <item>FanucSeries0i: 所有的方法实现异步接口，并增加了 RPC 的特性支持，方便直接注册就可以调用。</item>
	///             <item>SimensFetchWriteServer: 修复在standard项目里没有添加的bug。</item>
	///             <item>MelsecMcNet: 修复ReadMemory的报文错误，增加读取智能模块的ReadSmartModule方法。</item>
	///             <item>NetworkDataServerBase: 所有的虚拟PLC的基类，添加获取所在在线客户端信息的属性 GetOnlineSessions</item>
	///             <item>Demo: 所有读写PLC的界面在读写的时候，增加提示耗时的信息，包括最大值，最小值。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-3-15" version="9.6.5" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>SiemensS7Net: 修复上个版本的DB块位地址解析的bug，写入DB1.0.5为True的时候，却写入了DB1.0.0为True。</item>
	///             <item>AllenBradleyDF1Serial: 初步添加AB-PLC的DF1协议，支持了简单的读写，等待测试，地址示例：N7:0。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-3-25" version="9.6.6" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>SoftCRC16: 计算CRC16的辅助方法，开放预置值的设定，可以自由的指定。</item>
	///             <item>FanucSeries0i: 新增一个读取机床系统语言的api，读取之后将会自动切换语言，暂不支持根据消息自动匹配编码解析。</item>
	///             <item>SiemensS7Net: OperateResult&lt;byte[]&gt; Read( string[] address, ushort[] length )接口添加RPC支持</item>
	///             <item>NetworkDataServerBase: OnDataReceived事件签名修改为DataReceivedDelegate( object sender, object source, byte[] data )，追加一个source参数，可用来获取客户端IP地址，具体看api文档</item>
	///             <item>NetworkDoubleBase: 增加LocalBinding属性，如果需要绑定本地ip或是端口的，可以设置，所有的网络类PLC都支持绑定本地的ip端口操作了。</item>
	///             <item>NetworkUdpBase: 增加LocalBinding属性，如果需要绑定本地ip或是端口的，可以设置，所有的网络类PLC都支持绑定本地的ip端口操作了。</item>
	///             <item>SiemensS7Net: 完善异步的PDU自动长度信息，新增AI,AQ地址的读写，地址格式：AI0,AQ0，欢迎大家测试。</item>
	///             <item>OmronFinsNet: 欧姆龙FINSTCP协议的SA1机制调整为自动获取，不需要在手动设置，修复错误信息文本和错误码不匹配的bug。</item>
	///             <item>MqttClient: 修复在网络异常导致正在重连服务器的时候，调用ConnectClose方法后，后台仍然不停的重连服务器的BUG。</item>
	///             <item>NetworkDeviceSoloBase: 删除这个文件，并优化相关的串口透传类。全部改为继承自：NetworkDeviceBase</item>
	///             <item>NetworkDataServerBase: 所有派生类的虚拟服务器，包括modbus，s7, mc, fins等服务器全部支持设置是否允许远程写入操作，modbus的demo界面添加是否允许的选项。</item>
	///             <item>WebSocketClient: 修复客户量的Request报文少一个换行信号在某些服务器会连接失败的bug，新增两个发送数据的api，发送数据更加的灵活。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-4-15" version="9.7.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>OmronFinsNet, OmronFinsUdp, HostLink: 地址的解析优化，在读取的API方法里，自动按照500长度进行切割，可以由 ReadSplits 更改。</item>
	///             <item>FanucSeriesOi: Fanuc数控机床支持R数据的读取，参考API： ReadRData(int start, int end)</item>
	///             <item>HslExtension: 新增从字节数组获取位值的扩展方法: GetBoolValue( this byte[] bytes, int bytIndex, int boolIndex )</item>
	///             <item>FatekProgram: 地址解析优化，修复 RT,RC地址解析不正确的bug，读取的字及位长度自动切割，调用不受长度限制。</item>
	///             <item>SoftBasic: 添加设置byte数据的某个位的方法SetBoolOnByteIndex，也可以调用byte的扩展方法，byte.SetBoolByIndex(2, true) 就是设置第二位为true</item>
	///             <item>FujiSPHNet: 新增支持富士的SPH以太网协议，支持M1.0, M3.0, M10.0, I0, Q0地址的读写操作，支持位的读写操作。写位需要谨慎，先读字，修改位，再写入。</item>
	///             <item>net20, net35, net451三个框架版本的项目引用 http.web 组件，用来修复 HttpServer 里url携带中文时，会导致解析乱码的情况，现在支持了中文的api接口注册，中文参数。</item>
	///             <item>HttpServer: 使用了注册RPC接口时，返回调用方的数据内容格式调整为json格式，方便postman等测试工具识别内容。</item>
	///             <item>FujiSPHServer: 新增富士SPH协议的虚拟服务器，支持和FujiSPHNet进行测试通信。支持的地址是一致的。</item>
	///             <item>KeyenceNanoSerial: 基恩士的上位链路协议优化，支持了B，VB的bool读写，W，VM的字读写，新增bool数组写入功能。</item>
	///             <item>KeyenceNanoSerial: 支持了plc型号读取，状态读取，注释读取，扩展缓存器的读写，错误代码提示携带更详细文本，适用于 KeyenceNanoSerialOverTcp</item>
	///             <item>KeyenceNanoServer: 新增基恩士上位链路协议的虚拟服务器，可以和 KeyenceNanoSerialOverTcp 进行通信测试。</item>
	///             <item>KeyenceSR2000: 基恩士扫描的协议的错误提示信息新增了英文模式下的注释，原来的只有中文的提示。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-5-13" version="9.8.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>MqttSyncClient: ReadRpc&lt;T&gt;方法提示JSON转换错误消息更加友好，注释完善。</item>
	///             <item>MelsecMcServer: 三菱的虚拟plc新增对B，R，ZR的地址支持。</item>
	///             <item>FujiSPHNet: 富士PLC实现按照240长度自动切割，支持无限长的数据读取。</item>
	///             <item>MqttClient(不兼容更新): 接收服务器数据的事件签名修改，MqttMessageReceiveDelegate( MqttClient client, string topic, byte[] payload );</item>
	///             <item>WebSocketServer: 优化数据发送部分的功能逻辑代码，将数据发送从锁中解脱出来。</item>
	///             <item>MqttClient: 新增属性 ConnectionOptions 用来获取当前连接参数信息。</item>
	///             <item>Omron: Hostlink协议的读取字数据时，长度进行切割，按照260字切割，可通过ReadSplits属性修改。</item>
	///             <item>PanasonicMewtocolServer: 初步添加mewtocol的虚拟plc，初步测试R地址成功。</item>
	///             <item>设备通信核心: tcp, udp, 串口三大通信内核添加，封包和解包的虚方法，可以重写实现自定义需求。</item>
	///             <item>IModbus: 新增IModbus的设备接口，用来描述Modbus相关的设备，包含站号，DataFormat属性等。</item>
	///             <item>Modbus: 包含TCP,RTU,ASCII,RTU-over-tcp，UDP全部结构优化，重写，完善，最终一套代码实现覆盖以上类，接口无变化。
	///             但是如果用调用了ReadFromCoreServer则不兼容，现在都只需要传核心报文，01 03 00 00 00 01，无论rtu,tcp,ascii</item>
	///             <item>Modbus: 支持对寄存器，输入寄存器的位数据读取，ReadBool("100.1") 就是读取寄存器地址100的第一个位的bool值。</item>
	///             <item>MqttSubscribeMessage: Identifier默认设置为1，这样可以修复在某些服务器（mosquitto）订阅异常的bug。</item>
	///             <item>SerialBase(不兼容更新): ReadBase串口基本的交互方法重命名为ReadFromCoreServer，这样与TCP，及UDP的方法标准一致。</item>
	///             <item>FanucInterfaceNet: 支持 R 地址的读写，支持R1-R10，其中R1-R5为int数据，R6-R10为float数据。SR1-SR6进行字符串读写。</item>
	///             <item>BeckhoffAdsServer: 新增ADS的虚拟PLC，支持M100, I100, Q100地址格式。暂不支持内存地址，变量名。</item>
	///             <item>SiemensS7Net: 修复9.6.4版本添加的ConnectionType, LocalTSAP属性对 200，200smart型号的影响。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-5-23" version="10.0.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>V10版本：本来上个版本就应该定为V10版本，因为已经内核优化，出现不兼容更新，所以这个版本果断设定为V10版本。</item>
	///             <item>SerialBase: 修复在串口异常的情况下，会抛出异常的bug，是上个版本的问题，现在会返回失败的OperateResult对象。</item>
	///             <item>ShineInLightSourceController: 新增昱行智造科技（深圳）有限公司的光源控制器的通信对象，主要用于视觉的打光操作。</item>
	///             <item>MqttSession：Mqtt的会话信息增加一个object Tag属性，用来自己绑定一些自定义的数据。</item>
	///             <item>SerialBase: 串口初始化的方法修改为虚方法，允许在继承类里进行重写，修改一些默认参数信息。</item>
	///             <item>NetworkBase: 修复ReceiveAsync异步方法在length=-1时，对方关闭时返回仍然为成功的bug，只有在极少数情况下会触发。</item>
	///             <item>ModbusTcpServer: 新增一个属性UseModbusRtuOverTcp，只要设置为True，就可以创建ModbusRtuOverTcp的对应的服务器，使用TCP传送RTU报文。</item>
	///             <item>HttpServer: 新增SetLoginAccessControl( MqttCredential[] credentials )方法，用于增加默认的账户控制，如果传入null，则不启动账户控制。</item>
	///             <item>IReadWriteDevice: 新增设备读写接口，继承自IReadWriteNet，然后所有设备实现IReadWriteDevice接口，相关继承关系优化，接口增加ReadFromCoreServer。</item>
	///             <item>All: 统一所有的设备核心层打包报文方法名为:PackCommandWithHeader 解包的方法名为UnpackResponseContent，允许重写实现自定义操作。</item>
	///             <item>Omron: 对OmronFinsTcp和OmronFinsUdp的通信层大幅度优化，统一代码规则，新增run，stop，读取cpu数据，cpu状态的高级方法。</item>
	///             <item>DTSU6606Serial: 新增德力西电表的采集类，基于modbusrtu实现，ReadElectricalParameters方法可以直接获取电表相关参数。</item>
	///             <item>HslExtension: 有两个获取byte的位的方法，功能重复，删除GetBoolOnIndex方法，使用GetBoolByIndex方法。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-6-11" version="10.0.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>FatekProgram: 串口类和串口转网口透传类优化，统一一套代码来读写设备。</item>
	///             <item>IDisposable: NetworkAlienClient, NetworkAlienClient, LogNetBase, MqttClient, MqttServer, WebSocketClient, WebSocketServer实现释放接口。</item>
	///             <item>SiemensS7net: 新增DestTSAP属性，优化了LocalTSAP和DestTSAP属性对不同系列plc的值设置，当plc为s200系列时，支持设置自定义的值来访问plc。</item>
	///             <item>UltimateFileServer: 文件服务器删除目录所有文件调整为直接删除整个目录，新增支持删除指定目录下所有空的子目录的功能。文件客户端新增匹配操作的方法。</item>
	///             <item>PanasonicMcNet: 地址新增支持SD数据类型，示例SD0，返回的错误代码修改为松下的专用信息，和三菱的不一致。</item>
	///             <item>IModbus: Modbus接口新增TranlateToModbusAddress( string, byte) 接口，只要继承重写该方法，即可轻松实现自定义地址解析转modbus地址。</item>
	///             <item>Delta: 台达相关的类根据modbus最新的优化，全部进行优化，每个类只有一点点代码了。</item>
	///             <item>FujiSPB: 富士的串口协议代码和串口透传代码优化，修复串口类调用异步写bool失败的bug。</item>
	///             <item>XinJE: XinJEXCSerial重命名为 XinJESerial类，根据modbus的优化进行精简，支持了信捷系列选择，可选XC,XJ,XD，地址支持根据所选型号自动解析。</item>
	///             <item>XinJE: 新增基于串口透传的XinJESerialOverTcp类，以及modbustcp协议的XinJETcpNet类，DEMO上支持测试。</item>
	///             <item>Inovance: 汇川的类优化，删除原来的AM,H3U,H5U类，改用InovanceSeries枚举来区分系列，然后解析不同的地址。同时添加InovanceSerialOverTcp串口转网口类。</item>
	///             <item>OmronFinsServer: 欧姆龙的FinsTCP虚拟服务器端支持E数据块，E0.0-E31.0 都是指同一个数据块。</item>
	///             <item>IByteTransform: 新增二维数组的解析方法接口，主要是short,ushort,int,uint,long,ulong,float,double类型。</item>
	///             <item>Demo: MelsecSerialOverTcp的demo界面添加是否新版的选择。</item>
	///             <item>如果有用到汇川，信捷的类库，请注意升级时出现不兼容，需要修改下类型，指定PLC的系列，感谢支持。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-6-22" version="10.0.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>OmronFinsServer: 修复服务器的内存数据保存本地文件及加载文件时，不支持em区数据的bug。</item>
	///             <item>ISessionContext: 新增会话信息接口，在MqttServer和Httpserver中，在注册RPC时可以在方法参数里追加ISessionContext接口的上下文信息，用来控制当前api的对不同账户的权限。</item>
	///             <item>Modbus: TranslateToModbusAddress 单词拼写错误的修复。</item>
	///             <item>FujiSPBAddress: 地址类的继承改成 DeviceAddressDataBase</item>
	///             <item>ModbusHelper: 在所有modbus及派生类里，当实现地址转换后，修复写bool,bool[]时地址仍然不转换的bug。</item>
	///             <item>KeyenceNano: 新增 UseStation 属性，用来设定是否开启使用站号的报文功能，有些特殊的情况需要站号。</item>
	///             <item>KeyenceNano: 串口类和串口转网口透传类优化，统一一套代码来读写设备。</item>
	///             <item>其他的注释优化，代码优化</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-7-14" version="10.1.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Melsec: 三菱的MC协议TCP，UDP，二进制，ASCII代码优化，一套代码实现，新增IReadWriteMc接口，针对MC协议的设备通用读写类</item>
	///             <item>NetworkUdpBase: 新增LogMsgFormatBinary属性，可以指示当前的数据交互报文记录时按照ASCII编码显示，或是二进制显示。</item>
	///             <item>DTSU6606Serial: 德力西的电表的读取方法ReadElectricalParameters支持HslMqttApi特性，方便MRPC及WEBAPI接口服务注册。</item>
	///             <item>Demo: Modbus rtu的demo界面的报文读取取消crc封装，因为内部已经集成封装。</item>
	///             <item>Melsec: 统一 MelsecMcNet, MelsecMcUdp, MelsecMcAsciiNet, MelsecMcAsciiUdp, MelsecMcRNet的代码逻辑结构，修复了ASCII格式类的一些bug。</item>
	///             <item>MelsecMcServer: 三菱的虚拟服务器限制了bool读取长度7168限制字读取长度960，三菱MC客户端的bool读取支持自动切割。</item>
	///             <item>OmronHostLink: OmronHostLink及OmronHostLinkOverTcp代码优化，完善错误代码文本提示，增加返回命令和发送命令校验的操作。</item>
	///             <item>HslExtension: ToStringArray的扩展方法支持对GUID的解析功能，不支持.net20, .net35</item>
	///             <item>NetworkDataServerBase: 修复数据类服务器在主动关闭引擎时，在线客户端的数量未及时复原的bug，影响范围，所有的虚拟PLC服务器。</item>
	///             <item>OmronHostLinkServer: 新增欧姆龙HostLink协议的虚拟PLC，支持网口和串口的进行读写操作。优化hostlink协议的客户端错误代码含义展示，优化数据接收机制。</item>
	///             <item>Demo: httpclient界面支持对https接口测试，在内容请求的header支持添加content-type信息，提供了一些选项。</item>
	///             <item>SimensWebApi: 新增NetworkWebApiDevice设备类，实现IReadWritteNet接口，新增SimensWebApi类，用于西门子1500的webapi接口，可实现读写标签变量信息。</item>
	///             <item>FanucSeries0i: fanuc的通信类支持NC程序文件的上传和下载，删除，设置主程序号，启动加工操作。修复刀具信息读取时，某个刀具信息失败导致读取失败的bug。</item>
	///             <item>AllenBradleyServer: ab-plc的虚拟服务器支持会话id的生成，支持对客户端校验会话id是否一致。</item>
	///             <item>Melsec: MC协议的类支持对字地址按照位读取，例如读取D100.5 开始的3个位，使用ReadBool("D100.5", 3)即可</item>
	///             <item>NetworkBase: 优化ReceiveByMessage及异步版本的性能，减少一次内容数据的拷贝操作，提升内存利用效率，提升读写的性能。</item>
	///             <item>SiemensS7Net: 西门子s7协议的地址支持 VB100, VW100, VD100的写法。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-7-18" version="10.1.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>UltimateFileServer: 支持获取指定目录的所有文件大小，客户端IntegrationFileClient实现相应的调用方法GetTotalFileSize</item>
	///             <item>MqttServer: 支持获取指定目录的所有文件大小，客户端MqttSyncClient实现相应的调用方法GetTotalFileSize</item>
	///             <item>MelsecA3CNet1重命名为MelsecA3CNet，MelsecA3CNet1OverTcp重命名为MelsecA3CNetOverTcp，所有引用这两个类的话无法兼容更新。</item>
	///             <item>MelsecA3CNet,MelsecA3CNetOverTcp修复和校验bug，支持是否和校验，支持格式1,2,3,4四种通信机制，已经通过单元测试。</item>
	///             <item>MelsecA3CServer: 新增三菱的A3C虚拟plc，支持是否和校验，格式1，2，3，4，支持和MelsecA3CNet,MelsecA3CNetOverTcp通信测试。</item>
	///             <item>FanucSeries0i: fanuc机床客户端新增ReadCutterNumber读取当前刀具号的API信息。</item>
	///             <item>MqttClient: 新增属性UseTimerCheckDropped是否启动定时器检测，其他优化。</item>
	///             <item>SoftBasic: 新增一个静态辅助方法string GetAsciiStringRender( byte[] content ); 用来获取ASCII显示的，如果遇见不可见字符，则用十六进制替代。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-8-3" version="10.1.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>feat(UltimateFileServer): 获取文件服务器的指定目录的大小的方法改为获取大小，文件数量，最后一个文件修改时间的方法，客户端同步更新GetGroupFileInfo。</item>
	///             <item>feat(MqttServer): 获取文件服务器的指定目录的大小的方法改为获取大小，文件数量，最后一个文件修改时间的方法，客户端同步更新GetGroupFileInfo。</item>
	///             <item>feat(UltimateFileServer): 新增一个获取指定目录所有子目录的基本信息，包含总大小，文件数量，最后一个文件修改时间，GetSubGroupFileInfos</item>
	///             <item>feat(MqttServer): 文件服务器新增一个获取指定目录所有子目录的基本信息，包含总大小，文件数量，最后一个文件修改时间，GetSubGroupFileInfos</item>
	///             <item>SiemensWebApi: 修复在.net standard2.0及2.1中未添加SiemensWebApi类的bug。</item>
	///             <item>MelsecA3CNetHelper: 优化数据解析时，对错误异常的处理，增加错误捕获。</item>
	///             <item>LSisServer: 重写串口Cnet协议的数据接收，处理，和返回，支持了单变量数据，和连续数据的读写操作。</item>
	///             <item>XGBCnet, XGBCnetOverTcp: 重新实现了类，统一了代码，重新解析的数据内容，支持了对错误码的提取和错误消息的解析。</item>
	///             <item>memobus: 新增memobus通信协议，初步实现了读取的操作，具体还需要测试，如有网友有测试条件，可以联系我测试。</item>
	///             <item>Yamatake: 新增山武的数字示波器的CPL协议的通信对象和虚拟服务器，分别是DigitronCPL,DigitronCPLServer</item>
	///             <item>MqttServer, HttpServer: 使用HslMqttApi特性注册远程RPC接口时，支持对异步方法(async...await)进行注册，并进行异步调用，返回相关数据。仅支持NET451及以上。 </item>
	///             <item>HslMqttApi: 在注册RPC接口时，增加了对方法签名的解析过程，客户端可以浏览服务器接口的方法签名，清楚的看到返回类型，参数类型信息。</item>
	///             <item>Delta: DeltaDvp系列的网口，串口协议，修复在跨区域读写M1536及D4096时，地址偏移不正确的bug。现在会自动跳转。</item>
	///             <item>MqttServer: 修复Mqtt客户端在取消订阅时，传入多个的Topic时导致服务器解析异常的bug。</item>
	///             <item>XGBCnet: 支持Read(string[]); 读取多个地址的数据信息，自动按照每16进行拆分访问。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision> <revision date="2021-8-23" version="10.1.3" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>JSON: .NET framework的dll对newtonsoft.Json不依赖特定的版本。</item>
	///             <item>XGBFastEnet: 修复读取单个的bool时报文不正确的bug.</item>
	///             <item>MqttServer: 新增GetMqttSessionsByTopic方法，用来获取订阅某个主题的所有客户端列表。</item>
	///             <item>HttpServer: 修复httpserver中文编码问题，在谷歌，微软浏览器下显示中文乱码的bug。因为Content-Type传值不正确</item>
	///             <item>HttpServer: 修复在账户验证模式下，使用ajax跨域请求OPTIONS导致401错误的bug。</item>
	///             <item>FujiCommandSettingType: 新增富士的CommandSettingType通信协议的实现，基于TCP访问，支持地址见API文档.</item>
	///             <item>FujiCommandSettingTypeServer: 新增富士的CommandSettingType协议的虚拟PLC，支持B,M,K,D,W9,BD,F,A,WL,W21地址</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登字第5219522号，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-9-3" version="10.1.4" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>NetworkAlienClient: DTU(异形)服务器增加对报文的固定头的检查。</item>
	///             <item>NetworkServerBase: 连接异形DTU(异形)的server的方法ConnectHslAlientClient支持密码输入。</item>
	///             <item>NetworkDoubleBase: 当设置DTU会话时，修复恰好正在读取导致报文错乱的bug，初始化成功才成功切换DTU。</item>
	///             <item>McServer: 修复三菱的MC虚拟服务器在ASCII模式下，远程客户端读写B继电器时，地址解析异常的bug。</item>
	///             <item>LSisServer: 修复LSisServer在客户端读写位时，PX20之类的地址时，写入true不成功的bug。</item>
	///             <item>TemperatureController: 新增RKC的数字式温度控制器的读写类，地址支持M1,M2,M3,等等</item>
	///             <item>TemperatureControllerOverTcp: 新增RKC的数字式温度控制器的网口透传读写类，地址支持M1,M2,M3,等等</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-9-12" version="10.2.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>NetSoftUpdateServer: 代码优化，新增一个OnlineSessionss属性，用来获取当前正在更新的客户端的数量。</item>
	///             <item>RSAHelper: 提供了从PEM格式的私钥和公钥创建RSA对象的辅助方法，还提供了RSA对象导出PEM格式的私钥公钥方法。支持加密解密超长数据</item>
	///             <item>RSACryptoServiceProvider: 增加RSA对象的扩展方法GetPEMPrivateKey及GetPEMPublicKey方便快捷的获取PEM格式的公钥和私钥，扩展加密解密超长数据。</item>
	///             <item>SerialBase: 串口基类新增虚方法CheckReceiveDataComplete用于检查报文是否接收完成，一旦接收完成，立即返回，增加通信性能。</item>
	///             <item>ModbusRtu: 重写了CheckReceiveDataComplete方法，根据功能码的不同来判断当前的数据长度是否完整，以此判断报文是否完整。</item>
	///             <item>ModbusAscii: 重写了CheckReceiveDataComplete方法，根据开头及结尾的固定字符来是否指定值，以此判断报文是否完整。</item>
	///             <item>ModbusTcpServer: 优化服务器的串口接收功能，现在回复报文很快。1. 先接收串口数据，再Sleep。2. 增加数据完整性校验，一旦成功，立即返回报文。</item>
	///             <item>ModbusTcpServer: 新增属性RequestDelayTime，设置非0时用来防止有客户端疯狂进行请求而导致服务器的CPU占用率上升问题。</item>
	///             <item>MelsecA1EServer: 新增MC-A1E协议的虚拟服务器，支持了二进制和ASCII格式，已经配合客户端通过单元测试。</item>
	///             <item>AesCryptography, DesCryptography: 新增AES及DES加密解密对象，使用密钥实例化即可加密解密操作。</item>
	///             <item>MQTTServer: 在原有的基础上增加了加密的功能，如果MQTTClient，MQTTSyncClient启用加密功能，那么数据的传输就是加密的，无法抓包破解账户名密码及交互数据。</item>
	///             <item>AllenBradleyNet: cip协议支持了日期，日期时间的读写操作，这样omron的plc的cip协议也支持了日期时间的读写，在omroncip的demo上添加测试操作。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-9-19" version="10.2.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>SiemensS7Net: 修复读写WString字符串时，乱码的情况。</item>
	///             <item>NetSoftUpdateServer: 修复在某些情况下在线客户端数量新增后不会减少的bug。</item>
	///             <item>demo: 字节转换工具界面增加字节数组和base64字符串的转换功能。</item>
	///             <item>MqttServer: 当MQTTClient在加密模式下，订阅一个主题后，修复服务器仍然返回没有加密的bug，导致客户端解密失败。</item>
	///             <item>MqttSyncClient: 修复加密模式下，使用SetPersistentConnection设置长连接，不进行ConnectServer直接第一次请求失败的bug。</item>
	///             <item>AllenBradleyNet: 优化读取bool的功能方法，新增读取bool数组的实现。</item>
	///             <item>NetworkDataServerBase: 所有串口类的服务器（从机），功能代码优化调整，部分的类实现报文完整性判断，实现数据瞬间回复客户端（主机）。</item>
	///             <item>Serial: 大量的串口类的设备进行了优化，对接收结果是否结束进行判断，提高了串口通信的性能。</item>
	///             <item>NetSoftUpdateServer: 新增另一种更新机制，更新软件在收到文件信息后，先比对MD5信息来确定是否下载更新，从而提高升级速度，仍然兼容旧的更新模式。</item>
	///             <item>NetSoftUpdateServer配套的更新客户端，软件自动更新重新命名为 AutoUpdate, 针对差异化更新做了优化。</item>
	///             <item>DEMO里面所有的读写PLC界面的读写字符串功能支持了可选编码，支持ASCII,UTF8,UNICODE,UNICODE-BIG,GB2312,ANSI</item>
	///             <item>其他一些细节的优化，和注释的完善。DEMO界面的大量优化，显示调整，DEMO使用了新的更新软件，AutoUpdate.exe</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-9-30" version="10.2.2" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>NetworkDoubleBase: 在异步的网络通信方法中，原来的同步锁会在特殊的情况下导致UI线程卡死，所以改为异步锁，相关继承类也修改。</item>
	///             <item>HslReflectionHelper: 通过HslDeviceAddressAttribute反射Read，Write的读写自定义对象的功能支持对byte类型的读写操作，需要通信对象本身支持才能成功读写。</item>
	///             <item>SerialBase: 新增protect级别的AtLeastReceiveLength变量，用来表示从串口中至少接收的字节长度信息，一般为1。</item>
	///             <item>IReadWriteNet: 新增api支持ReadCustomer( string address, T obj )，允许传入实例对象，对属性进行赋值，方便wpf进行数据绑定操作。</item>
	///             <item>NetworkWebApiBase: 新增属性UseEncodingISO，在访问某些特殊的API的时候，会发生异常"The character set provided in ContentType is invalid...."，这时候本属性设置为true即可。</item>
	///             <item>Cip: 欧姆龙cip及rockwell的cip支持读取plc型号的方法ReadPlcType()，omron的cip重新设计了WriteTag，对于0xD1类型数据，支持偶数个写入操作。</item>
	///             <item>SiemensPPI: 修复writebyte方法的api接口名称还未注册的问题, 和串口透传类的相关代码优化，精简。</item>
	///             <item>MelsecFxSerial: AtLeastReceiveLength变量设置为2，和串口透传类的相关代码优化，精简。</item>
	///             <item>MqttServer: 新增属性：TopicWildcard，当设置为true时，支持主题订阅通配符，使用#,+来订阅多个主题的功能。具体参考该属性的API文档。</item>
	///             <item>demo: 修复demo的HTTPClient界面浏览在linux创建的Webapi服务器的api列表功能失败的bug，使用HttpWebRequest来实现。</item>
	///             <item>demo: rsa加密解密算法测试界面实现签名和验签操作。签名算法可选SHA1，SHA256, SHA512, MD5等等。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-11-19" version="10.3.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>allenbrandlyNet: ExtraOnDisconnect方法增加socket的空检测操作，因为在极少数特殊情况会发生空的异常的bug。</item>
	///             <item>OmronCipNet: 修复Write数据类型为short[]时长度不是1的bug。同步方法调用Write(string,short[])时会发生写入失败。</item>
	///             <item>Demo: OmronHostLinkCModeOverTcp的Demo测试界面添加读取型号的功能，可以更加便捷的测试。</item>
	///             <item>AllenBradleyNet: 重写读取字符串方法ReadString( string address, ushort length )，编码修改为UTF8</item>
	///             <item>ReverseWordTransform: 字节变换的对象代码优化精简，ReverseWordTransform新增IsInteger16Reverse属性指示short,ushort是否发生翻转，默认翻转。适用极个别特殊的Modbus格式</item>
	///             <item>YRC1000TcpNet: 安川的机器人的API功能大量添加，支持读取机器人坐标，各种变量信息，启动程序，停止，复位，读报警等待。</item>
	///             <item>SiemensS7Net: 写入bool数组的方法优化，修改为先读取byte[], 修改中间的位，然后再写回去操作，可以写入一个字节的中间几个位，单元测试通过，仍然有风险，谨慎调用。</item>
	///             <item>OmronHostLinkCMode: 修复OmronHostLinkCMode读写字节顺序颠倒的问题，新增了读取plc型号，plc状态，修改状态的功能，增加错误码文本解释。</item>
	///             <item>OmronHostLinkCModeServer: 新增CMode协议对应的虚拟PLC服务器，初步实现了和客户端通信功能，包括串口和网口的支持，单元测试通过。</item>
	///             <item>添加在NET2.0，NET3.5及Standard项目缺失的SiemensFetchWriteServer, MelsecA3CServer, MelsecA1EServer, 以及安川的 memobusTcpNet</item>
	///             <item>SiemensS7Net: 在读取单个的bool的值的时候，增加对结果数据的合法性检测，当遇到错误码时，提示错误信息。修复写入bool在某些特殊的情况下，实际成功但是提示失败的bug。</item>
	///             <item>YRCHighEthernet: 安川机器人添加高速以太网通信，基于UDP实现，在DEMO界面可以直接原生命令测试，支持读取报警，字节，整型，字符串等变量。</item>
	///             <item>PipeSerial, PipeSocket: 新增串口管道和网口管道，串口基类和网口基类优化重构，允许多个串口设备对象共享一个串口通道，多个网口的设备对象共享一个网口通道。</item>
	///             <item>ModbusTcpNet:新增属性IsCheckMessageId，用于配置modbustcp协议是否检查设备返回的消息ID是否一致，默认检查，设置为False，也即是不检查ID一致。</item>
	///             <item>MemobusTcpNet: 安川PLC的memobus协议的修复报文错误的bug，增加了线圈读写，寄存器读写，扩展寄存器的读写功能，例如扩展保持寄存器地址：x=9;100</item>
	///             <item>Demo：测试界面添加了一个TCP转TCP的界面，可以用于两个网络连接中捕捉通信的数据报文。还有其他的一些细节优化。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-12-2" version="10.4.0" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Delta:台达系列的PLC类重命名，新增PLC系列，可选DVP系列和AS系列，相当于新增了对AS300的支持，如果用到，谨慎更新。</item>
	///             <item>WebSocketServer: 原先订阅的操作使用header来完成，新增客户端从url添加订阅主题，例如：ws://127.0.0.1:1883/HslSubscribes=A,B</item>
	///             <item>AllenBradleyPcccNet:新增AB的CIP协议PCCC格式的通信，测试适用ab1400，地址格式为 N7:5, I:0/2, F2:0 等</item>
	///             <item>AllenBradleyPcccServer: 新增PCCC的虚拟服务器功能，可以配合客户端进行通信的测试，支持基本的地址。</item>
	///             <item>MC-A3C: 修复A3C的服务器在格式4的情况下，返回写入操作的控制代码错误的bug，应该为06，写成了02。</item>
	///             <item>三菱的A3C协议的读取字数据及位数据长度自动切割，可以输入理论最大长度，修复校验是否写入成功确提示失败的bug。</item>
	///             <item>NetworkDataServerBase:串口接收增加最小接收时间，默认是20，表示20毫秒，如果重写了报文结束检查，则可以设置大一点。</item>
	///             <item>NetworkServerBase: 基础服务器对象新增属性EnableIPv6，当设置为true的时候，则使用IPv6协议进行数据通信和访问。</item>
	///             <item>所有的TCP及UDP的相关的客户端通信类的 IP 地址设置，都支持了 IPv6 地址，所以目前IP地址支持 IPv4 和 IPv6 及 网址。</item>
	///             <item>Demo:fanuc机器人的测试界面写入数据操作进行提示相关的确认，防止手抖不小心写入导致机器人运行不正常。</item>
	///             <item>Demo，所有的设备通信类，新增一个支持的设备列表功能，大家可以手动添加支持的列表，方便别人查看支持的型号，请大家务必真实填写。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-12-17" version="10.4.1" author="Richard.Hu">
	///         <list type="bullet">
	///             <item>Delta: 台达的DVP系列，当地址带站号时，并且出现了跨地址切割时，修复站号丢失的bug。跨地址指跨越M1536,D4096地址。</item>
	///             <item>Delta: 台达AS系列的地址 SR 地址支持输入 SR0 以及 sr0 </item>
	///             <item>针对FinsTcp协议及ADS协议的剩余字节解析，本来是int类型，现在加了最大长度10000的限制，防止异常攻击时导致申请内存过大而奔溃。</item>
	///             <item>Inovance: 汇川相关的PLC的数据排列规则调整为 CDAB，DEMO上的选择也默认改为这个。</item>
	///             <item>MelsecFxSerial: 移除三菱编程口协议最小接收回复报文2字节的限制，实际测试有时候只需要一个字节就可以了，否则会一直处于接收超时。</item>
	///             <item>ByteTransform: TransBool的方法的偏移和长度均修改为以位为单位，具体调用API时查看注释说明，长度为10就表示10个长度的bool。</item>
	///             <item>AllenBradleySLCNet,AllenBradleyPcccNet: 地址支持ST10:2这种字符串地址，并在AllenBradleyPcccNet上实现动态读取字符串，长度为0或没有，则自动长度。</item>
	///             <item>AllenBradleySLCNet,AllenBradleyPcccNet: 以及SLC的协议地址支持了 L17:0 的地址，长整型类型</item>
	///             <item>VigorSerial,VigorSerialOverTcp: 新增丰炜PLC的编程口协议及透传类，支持VS系列，地址支持动态站号信息，例如 s=2;D100</item>
	///             <item>VigorServer: 新增丰炜的虚拟PLC，模拟了VS系列的通信，可以和对应的客户端进行数据读写测试，位地址支持 X,Y,M,S，字地址支持 D,R,SD</item>
	///             <item>MelsecFxSerial: 三菱的编程口协议及其透传类添加一个激活PLC的API方法ActivePlc，在某些特殊的PLC读写数据之前，需要先调用一下。</item>
	///             <item>Modbus:Modbus所有协议的ReadFromCoreServe(byte[])增加特别的注释，只需要传递modbus核心报文即可，不管tcp,还是rtu,还是ascii，都是一样的。</item>
	///             <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///             <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2021-12-22" version="10.4.2" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>NetworkBase: 新增Receive原始字节数据时，也就是申请byte[]缓存时，对可能存在的异常捕获操作，返回失败的结果对象。</item>
	///            <item>AllenBradleyPcccNet: 修复对于ReadAsync也注册了API接口，导致重复注册的bug。</item>
	///            <item>WebSocketClient: 修复因为掉线重连服务器的时候，重新携带订阅的主题。</item>
	///            <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2022-1-1" version="10.4.3" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>SerialBase: SetPipSerial重，命名为SetPipeSerial，如果有使用串口管道，则需要更改相关的名称。</item>
	///            <item>MelsecMcDataType: 修复三菱累计定时器当前的值的地址进制转换应该为10进制，结果写成100导致转换失败的bug。</item>
	///            <item>Keyence: 上位链路协议的串口及网口的通信类，ByteTransform的IsStringReverseByteWord调整为true，读写字符串时将两两颠倒。</item>
	///            <item>IByteTransform: 转换接口类的注释进行完善，提示更加详细完整，中英文并行提示。</item>
	///            <item>Vigor: 丰炜PLC的读取位和读取字的功能方法，对读取长度进行内置切割，相当于支持了无限长度的数据读取。</item>
	///            <item>EstunTcpNet: 新增埃斯顿机器人通信类，内置定时器保持心跳，支持读取机器人的基本信息，详细见DEMO界面。</item>
	///            <item>FanucInterfaceNet: 修复fanuc机器人的中文编码异常的bug，使用标准的GB编码解析，如果编码获取异常，需要自行nuget安装System.Text.Encoding.CodePages组件，并注册编码。</item>
	///            <item>Device: 设备类增加ReadStruct{T}方法，根据特性从原始字节里解析出实际的数据对象。影响范围所有的设备类对象。</item>
	///            <item>Demo: Demo程序支持了手动设置版本更新忽略提醒，忽略之后再菜单栏进行提示新版，以及增加添加激活操作功能，本地保存加密的激活码。</item>
	///            <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2022-1-17" version="10.5.0" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>ABBWebApiClient: GetRobotTarget方法，返回的数据信息，增加q4数据信息。</item>
	///            <item>Modbus: 在modbus的派生类协议中，重写了modbus的地址转换的情况中，修复读写bool操作，因为地址中带小数点导致地址转换异常的bug。例如汇川AM系列读写QX3000.0 的bool值</item>
	///            <item>MelsecFxSerialHelper: 三菱编程口协议的检查和校验的方法，增加try...catch，在某些特殊的情况下，会导致异常，直接奔溃。</item>
	///            <item>NetSoftUpdateServer: 针对之前旧版的软件升级功能，增加30分钟的超时时间限制，如果30分钟后仍然没有更新完成，则自动移除会话。</item>
	///            <item>SiemensS7: S7的地址支持大小写，且都支持带X,B,D,W的地址，比如MD100, MW100, MX100.2</item>
	///            <item>OmronConnectedCipNet: 注册报文里的不通信超时修改为42分钟，读取short，及ushort数组时，按照994长度进行切割</item>
	///            <item>AllenBradleyPcccNet: 支持使用0F-AB的掩码写入功能，写入一个bool值到PLC中，PCCC虚拟服务器实现了这个AB功能码，可以虚拟测试。</item>
	///            <item>FanucInterfaceNet: 修复demo上读取WO数据时，地址偏移不正确的bug。</item>
	///            <item>Freedom: 串口，网口的自由通信协议增加委托CheckResponseStatus，可以自定义对报文结果进行校验，完善注释。</item>
	///            <item>DLT645: 优化数据接收部分的代码，如果数据完整，立即返回，数据前面兼容无用的字符数据。</item>
	///            <item>INetMessage: 消息类新增方法PependedUselesByteLength( byte[] headByte )并在DLT645OverTcp消息类重写，支持前置无效的字符。</item>
	///            <item>AllenBradleyNet：支持添加消息路由功能，默认不开启，实例化属性MessageRouter, 例如：1.15.2.18.1.1，支持在demo界面进行配置操作。</item>
	///            <item>AllenBradleyNet: 支持遍历全局变量和局部变量。新增StructTagEnumerator( AbTagItem structTag )方法遍历结构体的成员变量信息。</item>
	///            <item>Demo:AllenBradleyNet节点浏览的界面支持了查找数据，显示数据，结构体嵌套遍历，还支持显示当前的数据信息。</item>
	///            <item>ModbusTcpServer的RTU接收时间调整为500ms，如果报文完整立即接收结束。</item>
	///            <item>本版本可能是春节前的最后一个版本了，提前祝大家新春快乐。</item>
	///            <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2022-2-28" version="10.5.1" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>AllenBradleyNet: Write( string address, bool value )写入bool方法优化，如果是类型代号0xD3的，地址前面需要增加 "i=" 标记。</item>
	///            <item>PanasonicMewtocolServer: 松下虚拟PLC地址类型支持完善线圈支持X,Y,R,L, 字单位的整型支持 X,Y,R,L,D,LD,F，支持RCP及WCP指令离散读写线圈。</item>
	///            <item>PanasonicMewtocol：松下协议及其网口类的代码优化提炼共同代码，新增加离散bool地址的读写，传入多个地址，返回一个bool数组。</item>
	///            <item>AsciiControl: 在命名空间slCommunication.Core下面增加一个AsciiControl类，包含控制类ASCII常量定义，例如ENQ,NAK,STX,ETX等。</item>
	///            <item>MelsecFxLinksServer: 新增三菱的FxLinks计算机链接协议的虚拟PLC，支持串口和网口透传访问，支持格式1，4切换，支持是否和校验。</item>
	///            <item>MelsecFxLinks: 1. 同时支持了格式1，格式4，2. 支持了地址大于10000的时候使用QR,QW命令，3. 修复报文创建时数据长度及站号不是16进制的bug。</item>
	///            <item>KeyenceNanoServer: 修复启动串口时，和上位链路客户端通信时，一直返回通信校验错误的bug，现在针对CC指令和CQ指令都能正确的返回。</item>
	///            <item>keyenceNano: 基恩士上位链路协议的串口类和网口透传类记录报文的格式调整为ASCII码，这样更加直观。</item>
	///            <item>NetworDataServerBase: 添加GetNewNetMessage( )及ReadFromCoreServer方法，精简所有继承的子类虚拟PLC的服务器的代码。</item>
	///            <item>MelsecA1EServer: 修复三菱A1E协议服务器报文接收异常的bug，导致客户端读写数据不正常。</item>
	///            <item>FatekPrograme: 永宏编程口协议读取字按照64字长度自动切割，支持了RUN,STOP，读取状态接口方法，新增对应的虚拟服务器实现。</item>
	///            <item>SpecifiedCharacterMessage: 新增基于指定字符结尾的消息类，通过ProtocolHeadBytesLength属性变种而来，NetworkBase的ReceiveByMessage功能适配了SpecifiedCharacterMessage消息。</item>
	///            <item>Turck: 新增图尔克的Reader协议实现，支持对字节读写，bool读写，实现了虚拟服务器，通过了单元测试，主要用来和RFID进行通信。</item>
	///            <item>IEC104: 初步添加IEC104协议实现和解析，增加demo测试，但是目前还清楚怎么设计API和使用场景，等待继续优化，欢迎相关需求的人联系测试优化。</item>
	///            <item>DEMO: RSA加密解密的测试界面，支持对超长的数据进行加密解密操作。</item>
	///            <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2022-4-1" version="10.6.0" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>ModbusHelper: 修复ReadBoolHelper在Modbus继承类里进行了自定义地址转换，导致读字地址的位失败，例如汇川AM系列读取M240.1的bool数据失败</item>
	///            <item>LSisServer: 修复基于Cnet串口协议的通讯时，客户端使用SB连续读取指令读取长度大于9时，服务器对长度解析失败导致读取失败的bug。</item>
	///            <item>GroupFileContainer: 新增获取所有文件的下载次数总和的属性，名称为：TotalDownloadTimes。</item>
	///            <item>SerialPort: 新增串口的扩展方法IniSerialByFormatString，支持格式化的初始串口信息，例如 COM3-9600-8-N-1，简单明了。ModbusServer界面支持串口参数配置，支持配置最短接收时间配置。</item>
	///            <item>MelsecFxLinksServer: 三菱Fxlinks虚拟服务器，修复远程客户端在读取位地址（例如M100）的字数据时，返回错误失败的bug。</item>
	///            <item>HslReflectionHelper: MRPC及Webapi注册的方法接口功能中，支持参数类型为自定义类型数组的参数，例如Student[]，不支持List{Student}。</item>
	///            <item>MqttServer: 文件读写功能的引擎中，遍历所有子目录统计信息时，可选参数是否携带返回最新的一个文件的信息，客户端做了相关的适配，参数支持。</item>
	///            <item>XGBFastEnet: 修复向PLC读取数据到时候，类型指定不正确的bug，之前无论是什么类型，都会设置为0，PLC从而值返回一个字节长度的数据。</item>
	///            <item>Mewtocol: 松下Mewtocol协议，当读写字单位超出协议限制时，自动升级为扩展协议标识，标记字符为 &lt; (小于号), 服务器读写长度根据实际限制，支持扩展协议标识。</item>
	///            <item>FanucSeries0i: 修复fanuc机床类读取虚拟机床系统时，显示运行时间为负数的bug。</item>
	///            <item>NetworkDoubleBase: 新增受保护的属性UseServerActivePush，只要设置为True，就可以将当前的连接切换为既支持设备主动发送，又支持同步访问的客户端，使用信号同步器来支持问答通信。</item>
	///            <item>SiemensPPIServer: 新增PPI Server类，支持TCP以及串口的通讯，PPI的串口和网口透传类客户端增加读取PLC类型的功能方法。</item>
	///            <item>SiemensS7Server: 修复客户端读取数据时，返回客户端FF04的数据内容时，位长度信息赋值不正确的bug，这会导致某些客户端发生数据异常。</item>
	///            <item>SiemensPPI: 西门子PPI协议优化，读取bool[]的功能方法调整为先读取字，再解析出位数据信息，以此来支持批量读取位。</item>
	///            <item>XinJETcpNet: 信捷的modbustcp协议新增当某些地址超过临界范围时，自动升级modbus协议到信捷内部TCP协议，以此访问到更大范围的数据内容，并支持了临界地址的跨地址访问，自动切割重组。</item>
	///            <item>XinJEInternalNet: 新增完全的信捷内部协议实现的TCP通信，可以读取更大范围的数据内容，比如D200000地址，支持的地址参考API文档。</item>
	///            <item>DeltaDvp: 修复台达系列在进行读取D寄存器的位的时候，也即是时 ReadBool("D100.1") 方法时，地址无法识别的bug，现在可以正确的读到D100的位信息。</item>
	///            <item>FanucSeries0i: 新增WriteRData的接口方法，新增读写PMC数据的方法，ReadPMCData及WritePMCData，与R数据的读写类似，都是字节操作的方法。</item>
	///            <item>IReadWriteDevice: 扩充方法支持，支持ReadFromCoreServer(List{byte[]})的读取的方法支持，支持多个报文读取，结果打包返回。</item>
	///            <item>BeckhoffAdsNet: 倍福通信类新增是否使用自动AMS属性，默认为false，设置为true时，网络初始化时从server端加载相关的NETID参数信息，目前在twincat3上成功测试。</item>
	///            <item>BeckhoffAdsNet: 修复ReadBool数组时，返回的长度都是8的倍数的bug。属性UseServerActivePush调整为True，支持PLC方主动推送数据信息。</item>
	///            <item>BeckhoffAdsNet: 修改读取bool时地址机制，地址支持小数点，例如M100.2 ，所以M800等于M100.0， 修复读取数组时值解析不正确的bug， 修复写入bool数组失败的bug。</item>
	///            <item>BeckhoffAdsNet: 修复直接SetPersistentConnection设置长连接无法读取的bug，以及掉线后，继续读写一直失败的bug。</item>
	///            <item>BeckhoffAdsNet: 修复倍福服务器重启后继续读写标签变量一直失败的bug，原因来自倍福重启导致标签内存地址变更，但是缓存还是一直使用旧的。</item>
	///            <item>SecsHsms: 新增secs协议的hsms实现，初步实现了主动方，也即是客户端，支持了一个通用的接口和数据，ReadSecsMessage方法。</item>
	///            <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2022-4-11" version="10.6.1" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>MelsecMcRNet: 修复McType属性设置不正确的bug，导致依然使用普通的mc协议来进行通信，因为R系列MC协议地址不一样。</item>
	///            <item>ModbusServer: ModbusServer增加属性StationCheck，默认为True，对请求的客户端的站号进行检查操作，也可以设置false，不检查站号一致。</item>
	///            <item>BeckhoffAdsNet: 新增支持批量读取的方法Read( string[] address, ushort[] length )，完善了API文档说明，可以一次性读取多个标签信息，需要自行解析。</item>
	///            <item>BeckhoffAdsNet： 修改默认的端口为851，主要用于TWINCAT3，当没有设置 NETID 时，直接连接PLC的话，自动升级为自动获取NETID信息。</item>
	///            <item>BeckhoffAdsNet: 重写实现新的Read{T}(), 一次性读取所有的类型数据，然后自动解析赋值到对象的属性，在API文档上增加示例的代码。</item>
	///            <item>SiemensPPI: 西门子PPI协议修复输入M5这种只有两个字符的地址的时候，地址解析报异常的bug，同样也适用于SiemensPPIOverTcp</item>
	///            <item>KeyenceNano: 基恩士上位链路协议优化，地址重新设计，修复B,VB,W的十六进制地址解析异常的bug，FM地址不正确解析的bug。</item>
	///            <item>KeyenceNano: 上位链路协议读取支持自动切割，根据不同的地址切割出不同的长度信息，一般都是256长度切割，虚拟服务器也进行了地址修复和长度限制适配。</item>
	///            <item>OmronFinsUdp: 欧姆龙Finsudp协议的DA1（PLC节点号）调整为0x00，自动获取，在demo测试界面，增加GCT和SID的参数设置，一般默认就好。</item>
	///            <item>FanucSeries0i: 读取R数据的接口重新设计，修改为ReadPMCData，地址支持G,F,Y,X,A,R,T,K,C,D,E 例如R1200，写入也是一样，具体看demo演示。</item>
	///            <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2022-5-25" version="11.0.0" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>TcpForward: 新增TCP转TCP的类，设置本地端口及远程端口，客户端连接之后立即连接远程服务器，进行数据转发，支持多个连接，每个客户端都创建一个远程连接对象，DEMO的转发测试使用这个类。</item>
	///            <item>BeckhoffAdsServer: 倍福的虚拟PLC修复M100.1位地址读写数据不正确的bug，支持了符号地址，支持创建符号数据，自动生成唯一的句柄地址。支持客户端批量的地址读取，也即功能码为 0xF080。</item>
	///            <item>AllenBradleyPcccServer: 修复PCCC虚拟PLC读写地址数据解析异常的bug，导致和客户端的地址数据不一致。提炼优化地址信息。</item>
	///            <item>NetworkDataServerBase: 优化串口接收时候的数据检测的代码，修复modbus虚拟服务器接收串口数据时，功能码为0x10时，SerialReceiveAtleastTime属性设置无效的bug。</item>
	///            <item>XinJE: 基于modbus协议的信捷类对象，当地址是位地址时(X,Y,M等)，支持了当使用Read（字单位）读取原始字节的方法，返回原始的字节信息。</item>
	///            <item>FanucSeries0i: Fanuc数控机床通信类，新增读取机床型号信息的api，ReadSysInfo，信息在连接初始时获取生成，后续直接读取内存。</item>
	///            <item>FanucSeries0i: 读取程序文件及下载程序到机床支持路径参数，例如操作PATH2时只需要输入 //CNC_MEM/USER/PATH2/ 即可，新增DeleteFile的API用于删除任意路径的程序文件。</item>
	///            <item>FanucSeries0i: 新增ReadAllDirectoryAndFile方法，用来遍历指定路径下的所有的子路径和文件的信息，文件大小使用字节为单位。</item>
	///            <item>ISessionContext: 在MRPC的接口及HttpWebapi注册接口的方法里，参数ISessionContext新增Tag对象，方便自定义捆绑一些数据信息。</item>
	///            <item>MqttServer: 文件服务器相关的功能增加GetGroupFromFilePath方法，从路径信息里获取到文件列表管理容器，进而可以获取单个文件的对象，在权限控制时可以更加的细致。</item>
	///            <item>Modbus: ModbusHelper针对modbus-rtu的返回接收报文检测，增加对站号一致性的检测，如果发现站号不一样，返回错误信息。</item>
	///            <item>ICryptography: 添加直接对字符串进行加密解密的方法，并在AesCryptography类和DesCryptography实现方法，方便直接操作。</item>
	///            <item>OmronConnectedCipNet: 优化批量读取数组的方法，自动根据类型来切割不同的读取长度，分批次读取所有的数据信息。修复握手报文丢失Timeout时间报文的bug，导致v10.5.0以来不能读取的问题</item>
	///            <item>OmronConnectedCipNet: 优化和PLC持续几秒钟不读写就被PLC强制断线的问题，现在支持很长的时间，修复关闭立即重连（包括意外断线重连）连接不成功，一直报错重复打开会话的异常。</item>
	///            <item>NetworkConnectedCip: 基于连接的CIP协议里，在OpenForward初始化里，自动获取连接ID，如果失败，并尝试五次自增连接ID重新Open。</item>
	///            <item>AllenBradleyConnectedCipNet: 新增ab的connected cip协议的实现，继承自欧姆龙的connected cip协议，重写了部分的逻辑实现，缩小了单次读取的字节上限，初步测试通过。</item>
	///            <item>NetworkServerBase,NetworkDoubleBase: 新增属性SocketKeepAliveTime，用来设置底层的socket的KeepAlive信号，单位毫秒，默认不开启，开启后将会自动间隔的发送KeepAlive信号。</item>
	///            <item>AllenBradleyServer: cip的虚拟服务器同时支持了基于连接的CIP的访问。标签定义类AllenBradleyItemValue新增TypeCode属性，在返回客户端数据的时候，带类型数据返回。支持bool[]节点创建</item>
	///            <item>SoftBasic: GetAsciiStringRender 不可见字符判断除了0x20以下，追加0x7e以上的字符。新增GetFromAsciiStringRender方法，把GetAsciiStringRender 结果反向转换回来。</item>
	///            <item>AllenBradleyNet: 支持写入bool[]功能，支持写入byte[]数组功能（类型代号默认为0xD1），WriteTag方法支持地址携带类型信息，例如地址 type=0xD1;A[0]</item>
	///            <item>MelsecFxSerial: 三菱的编程口协议在新版协议配置为True时，修复D8000地址以上读写不正确的bug，修复X,Y,M,S部分地址读写不正确的bug，都调整为新版的报文，支持的范围更大更准确。</item>
	///            <item>MelsecFxSerialOverTcp: 新增加通过GOT连接的方式，。PanasonicMewtocol: 松下的Mewtocol协议支持了经过值及目标值。ILogNet: 日志的接口新增属性LogThreadID，用来配置是否记录线程ID的数据信息。</item>
	///            <item>SecsHsmsServer: 新增Secs协议的虚拟服务器，支持事件自定义处理所有的消息。并添加示例的代码。Demo程序界面。</item>
	///            <item>Mqtt: MqttClient及MqttServer支持了遗言消息，客户端A在连接参数里设置了遗言主题及数据，当客户端A非正常掉线的时候，服务器即发布该遗言主题信息。</item>
	///            <item>MqttSyncClient: 新增HslCancelToken对象，在文件上传和下载的过程中支持取消操作，MqttServer适配相应的功能实现，取消示例代码增加。</item>
	///            <item>HttpServer: 新增Action对象DealWithHttpListenerRequest，用来处理定义的Header数据到Session会话中，实现更加复杂自定义的操作。</item>
	///            <item>Demo: DEMO写入各种PLC的数据功能里，写入的数据支持byte数组，例如数据输入[1,2,3], 点击写入byte，就自动写入new byte[]{ 0x01, 0x02, 0x03};</item>
	///            <item>Demo: TCP调试界面优化，新增握手报文设置。新增一个测试正则表达式的界面，增加一些常用的正则表达式的内容。CIP浏览节点界面支持正则表达式筛选节点。</item>
	///            <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2022-6-9" version="11.0.1" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>HslExtension: 新增了一些扩展方法，现在可以直接从short，int类型获取位及设置位的功能，例如 bool value = shortValue.GetBoolByIndex(10); 设置位也是同理。</item>
	///            <item>DLT645: Demo界面优化，无论是串口的，还是网口的，都支持了密码及操作者ID的输入。修复了站号输入后，但是站号初始化不成功的bug。</item>
	///            <item>LogStatisticsBase: 当传入的长度为-1时，表示不限制长度信息，会一直的新增。</item>
	///            <item>Upgrade.exe: 用于自动升级更新的项目重新优化，支持了下载时候的网速显示，支持了下载百分比显示，删除了自动创建快捷方式的代码。</item>
	///            <item>SerialBase: 初始化串口的方法SerialPortInni( string portName )支持格式化的输入，例如COM3-9600-8-N-1，COM5-19200-7-E-2。</item>
	///            <item>MelsecFxSerialOverTcp: 当启动GOT透传时，修复报文封装不正确导致写入失败，读写值不正常的bug。</item>
	///            <item>InovanceHelper): 汇川的PLC针对M地址的寄存器，支持使用 MX0.0 来对位进行读取操作。支持了MB100读取byte类型的数据。</item>
	///            <item>Demo: 在Demo所有的设备通信测试界面，几乎所有的设备都支持了显示报文信息，想要查看通信的报文协议，菜单里点击 报文日志 。</item>
	///            <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2022-6-28" version="11.0.2" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>LogNet: 修复日志对象在配置了LogThreadID=False时，控制台输出界面仍然显示线程号信息的bug。</item>
	///            <item>AllenBradleyServer: 新增属性CreateTagWithWrite，当手动设置为true时，从服务器端写入不存在的标签将根据写入的类型自动创建标签。</item>
	///            <item>NetworkUdpBase: udp的通信基类修复某些设备通信情况下每9次通信就会跟随一次失败的bug，原因来自不停的创建socket，现在修改为连接正常就不重新创建socket</item>
	///            <item>AllenBradleyServer: ab虚拟plc修复写入short的类型分配不正确的bug，并且增加一个创建byte字节数组的标签的方法接口。</item>
	///            <item>AllenBradleyNet: 修复同步方法ReadString(string address); 读取字符串返回数据乱码的bug，原因来自调用了基类的字符串解析。</item>
	///            <item>DLT645: 大面积优化，支持了更多的地址读取，支持了一个地址多个数据读取，修复了部分数据(如电流，功率因数等)不识别正负号的bug。</item>
	///            <item>PipeSocket: 端口号信息由int型调整为数组，使用SetMultiPorts方法可以设置多个端口号信息，当PLC对象重连时，就会切换端口号，循环反复使用指定的端口号信息。</item>
	///            <item>PipeSocket: 多端口号使用方法为 plc.GetPipeSocket( ).SetMultiPorts( new int[] {6000, 6001} ); 实例化之后调用一次即可。</item>
	///            <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注。</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2022-7-22" version="11.0.3" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>MelsecFxSerialOverTcp: 修复在启用GOT透传时，碰到特殊的报文解析异常，最终导致偶尔读写失败的bug。</item>
	///            <item>InovanceHelper: 修复汇川PLC的AM系列时，读写M区域位地址的数据偏移不正确的bug，例如MX100.0，实际地址应该是MW50的第0位。</item>
	///            <item>ModbusTcpServer: 新增属性StationDataIsolation，默认为false，表示server只有一个数据区，所有站号用的一个数据区。如果设置为 true，表示server给每个站号开辟一个数据区。</item>
	///            <item>ModbusTcpServer: 修改Station站号类型为byte，删除属性StationCheck，现在不需要检查站号了。服务器侧读写数据支持了输入站号信息，例如 s=2;100，可以访问不同站号的数据。</item>
	///            <item>FujiSPBServer: 修复富士SPB虚拟服务器再读写位数据的时候，修复地址偏移解析异常导致bug。</item>
	///            <item>AllenBradleyServer: CIP协议的虚拟服务器修复当客户端写入bool变量时，无论写入True还是False，服务器都写入True的bug。</item>
	///            <item>MelsecFxSerialOverTcp: 三菱的编程口协议里，读取的地址长度突破了254字节的长度限制，现在可以读取无限个bool值，或是其他类型的值。</item>
	///            <item>HslReflectionHelper: 在MRPC及webapi接口的参数数据提取过程中，支持传入JSON对象及JSON对象的字符串，都会自动解析成正确的对象值或是JObject值。</item>
	///            <item>MelsecA1ENet: 三菱的A1E协议，读取字及位数据时，支持了读取超过64地址长度的数据，内部自动切割重组。</item>
	///            <item>DLT698: 初步添加DLT698.45的协议实现，使用明文的通信方式。支持读取功率，总功，电压，电流，频率，功率因数等数据。</item>
	///            <item>XGBCnet: Lsis的XGBCnet协议修复部分的bool读写位置和真实PLC对应不上的bug，bool的读取支持了MW100.2 带小数点表示的方式。</item>
	///            <item>XGBCnet: 支持了批量的bool数组读取功能（内部自动读取字节，解析出bool数组，不支持写入bool数组）。修复XGBFastEnet在配置XGB型号时读写数据异常的bug。</item>
	///            <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注，新官网：http://www.hsltechnology.cn/。</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>  <revision date="2022-7-27" version="11.0.4" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>NET451: 紧急修复NET451项目因为代码合并时导致签名丢失的问题，否则将导致签名的应用程序无法编译。</item>
	///            <item>DLT698OverTcp: 新增DLT698的串口透传类，使用串口转网口即可进行通信。</item>
	///            <item>DLT698TcpNet: 新增基于网口的DLT698协议，存在握手包，还未详细测试。</item>
	///            <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注，新官网：http://www.hsltechnology.cn/。</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2022-8-12" version="11.0.5" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>ModbusAsciiOverTcp: 新增modbusascii协议的串口转网口透传的通信类。</item>
	///            <item>DeltaSerialAsciiOverTcp: 新增一个台达的ASCII通信协议的串口转网口透传通信类，和其他的基于modbus的台达类一致的用法，一致的地址格式。</item>
	///            <item>FujiCommandSettingType: 修复FujiCommandSettingType及其虚拟服务器再解析地址不正确的bug，导致大量的其他地址解析失败，并完善了注释说明。</item>
	///            <item>MelsecFxSerial: 新增属性AutoChangeBaudRate，默认false，当设置为true时，在串口打开时会和PLC交互改变PLC默认的波特率。</item>
	///            <item>SerialBase: 新增串口类基类属性ReceiveEmptyDataCount，用来表示接收空白数据次数，然后判断接收完成的标记，单次耗费的时间是 SleepTime。</item>
	///            <item>Modbus: DEMO相关的界面都默认的DataFormat都调整为 CDAB，因为大多数的设备数据大小端都是这个格式。</item>
	///            <item>OmronConnectedCipNet: 新增IReadWriteCip接口，欧姆龙的基于连接的CIP协议实现了日期及时间的读写接口。</item>
	///            <item>MqttServer: MQTT服务器支持向自定义规则的客户端会话发布指定的主题数据，详细查看PublishTopicPayload重载方法</item>
	///            <item>MqttSyncClient: 上传文件到服务器的功能接口里新增流数据的上传，需要指定服务器保存的文件的名称。</item>
	///            <item>NetworkConnectedCip: 基于连接的CIP协议的连接id信息初始化时及赋予一个随机数，增加一个新的错误描述信息，连接过多的错误。</item>
	///            <item>Upgrade.exe: 用于更新的exe修复当文件长度为0时，导致百分比计算异常的bug。</item>
	///            <item>OmronFinsAddress: 欧姆龙FINS协议的地址，增加支持了定时器TIM,计数器CNT，索引寄存器IR，数据寄存器DR的读写。</item>
	///            <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注，新官网：http://www.hsltechnology.cn/。</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2022-8-18" version="11.0.6" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>TcpForward: TCP转TCP类新增OnRemoteMessageReceived及OnClientMessageReceive事件，可以捕获所有的通信报文。</item>
	///            <item>TcpForward: TCP转TCP的对象新增ConnectTimeOut属性，支持设定连接超时</item>
	///            <item>NetSoftUpdateServer: 更新的服务器新增一个方法，GetDealSizeAndReset每秒调用即可获取当前的下载网速信息</item>
	///            <item>AllenBradleyNet: 完善了路由信息，支持了一种特殊的路由信息的消息，格式为 1.1.2.130.133.139.61.1.0。</item>
	///            <item>TcpFoward: TCP转发的类新增一个属性OnlineSessionsCount，获取当前在线会客户端会话数量信息。</item>
	///            <item>SecsHsms: 新增属性 InitializationS0F0，默认为false，指示是否在初始化的时候，发送功能码 S0F0。</item>
	///            <item>Lognet: 优化日志存储器在计算过期日志文件并删除的代码，现在触发新增文件时，才进行删除旧的文件信息。</item>
	///            <item>官网地址： http://www.hslcommunication.cn 官网的界面全新设计过，感谢浏览关注，新官网：http://www.hsltechnology.cn/。</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2022-9-13" version="11.1.0" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>SiemensS7Net: 新增ReadDate，WriteDate的功能方法，实现了对日期类型的变量的读写操作，PLC测标记为D#2022-8-19</item>
	///            <item>SecsHsms: 客户端和服务器的程序，都支持了字符串编码属性设置，可以支持其他的编码，默认的编码调整为Default，主要是支持了中文的消息。</item>
	///            <item>FanucInterfaceNet: fanuc机器人的地址支持了直接的地址，Bool地址支持：SDO, SDI, RDI, RDO, UI, UO, SI, SO ,字单位地址支持：GI, GO, D。修复日志重复记录。</item>
	///            <item>OmronFinsNet: OmronFinsNet，OmronFinsUdp, OmronHostLink, OmronHostLinkOverTcp支持0104功能码，批量读取随机字地址，使用方法 Read(string[])</item>
	///            <item>OmronFinsServer: fins虚拟服务器支持了0104随机读取地址的功能码，对代码进行了优化，提炼了重复的代码。</item>
	///            <item>MqttRpcDevice: 新增基于MQTT-RPC接口的设备对象，用来访问使用MQTTServer注册plc对象为RPC接口的设备，让单连接的PLC瞬间支持N连接，参考：https://www.cnblogs.com/dathlin/p/16632767.html</item>
	///            <item>Toledo: 托利多电子秤增加通信报文日志记录，支持了扩展模式下的输出格式，并对数据分析增加异常捕获，日志记录。优化串口的通信接收，增加是否配置和校验的属性。</item>
	///            <item>MqttClient: 每个订阅的主题升级为SubscribeTopic类对象，携带一个事件，支持每个主题绑定不同的事件内容。方便子窗体使用同一个MqttClient对象订阅不同的主题并触发不同的事件。</item>
	///            <item>SiemensS7Server: 优化字符串的读写操作，支持WString字符串的读写，自动使用S7格式的字符串，在demo写入字符串数据的时候，客户端也可以正确的读取。</item>
	///            <item>ILogNet: ILogNet日志接口新增一个属性LogStxAsciiCode用来配置是否在每条日志开头记录0x02的ASCII码字符，默认为true，设置false后也就无法使用hsl自带的日志分析工具。</item>
	///            <item>RedisClient: redis客户端类支持了集群的服务器情况，当标签A在另一个服务器时，redisclient会自动连接到对应的服务器进行获取。</item>
	///            <item>FanucSeries0i: fanuc机床的通信类支持了ReadOperatorMessage方法，用来读取机床的操作信息。</item>
	///            <item>MqttServer: 修复Mqtt服务器在进行订阅结果反馈时，没有将topic主题的Qos也返回的bug，在某些情况下，客户端会引发异常。</item>
	///            <item>NetworkDataServerBase: 新增加属性ForceSerialReceiveOnce，默认为false，当多个modbusserver使用485总线串到一起时，需要设置为true</item>
	///            <item>Iec104: 修复连接失败的bug，修复在I帧消息号返回接收的id信息数量不正确的bug，现在可以正确的接收设备的数据。</item>
	///            <item>Demo界面新增一个各种校验码测试的界面，主要用于测试CRC16，LRC，FCS（异或校验）,ACC(和校验)，欧姆龙的Fins协议Demo界面上支持配置字符串是否翻转。</item>
	///            <item>Json: 依赖的json组件 Newtonsoft.Json 更新到最新的 v13.0.1 版本。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2022-10-12" version="11.1.1" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>Demo: 修复山武设备串口通讯调试界面，站号设置无效的bug。</item>
	///            <item>MelsecMcServer： MC协议的虚拟服务器支持L寄存器的读写操作，原先的D寄存器范围为 D0~D65535 扩展到 D0~D131071</item>
	///            <item>MqttSession: Mqtt会话新增属性DeveloperPermissions，标记开发者权限，服务器可以自由控制会话是否具有遍历MRPC接口的权限。</item>
	///            <item>DLT: DLT645及DLT698协议及其网口透传类新增属性：EnableCodeFE, 莫认false，当设置为True时每次发报文到设备时都会追加FEFEFEFE的指令头。</item>
	///            <item>OmronConnectedCipNet: 新增属性ConnectionTimeoutMultiplier用来设置异常超时时间，默认02表示32秒。</item>
	///            <item>SiemensS7Server: 优化握手报文的数据信息，现在支持了node-red的s7功能节点的读写数据，支持了s7netplus组件的连接及读写。</item>
	///            <item>SoftBasic: 新增从XML元素获取泛型值的便捷方法，DEMO程序的一些界面优化，串口调试界面优化，webapi服务器接口界面支持接口存储。</item>
	///            <item>OmronFinsTcp: FinsTcp协议优化，在接收fins消息报文时，即使前面有多个无用字节数据，依然可以正确的接收并处理数据。</item>
	///            <item>SiemensS7Server: 西门子的s7虚拟服务器的DB块优化，采用词典存储，内置DB1，2，3，其他DB块需要使用AddDbBlock方法来手动增加，否则无法读写。</item>
	///            <item>YokogawaLinkServer: 修复横河PLC虚拟服务器的读取bool数据时，无论输入什么地址类型，都会读取m寄存器的bug。</item>
	///            <item>HslExtension: ToHexString 的扩展方法以及SoftBasic.ByteToHexString 方法在转换byte[]到string时，支持格式化的参数，默认 {0:X2}</item>
	///            <item>SerialBase: 修复在特殊情况下读取数据导致没有响应，也不触发超时的bug，优化modbusrtu在错误码报文下的接收完成判断。</item>
	///            <item>MemobusTcpNet: 安川PLC修复读写扩展寄存器失败的bug，通信细节进行优化，支持对扩展寄存器的随机字读取和写入操作。并增加MemobusUdpNet类，基于UDP的实现。</item>
	///            <item>MemobusTcpServer: 新增加安川PLC的虚拟服务器实现，支持功能码为 01 02 03 04 05 06 08 09 0A 0B 0D 0E 0F 10</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2022-10-25" version="11.2.0" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>SoftBasic: 新增对url字符串编码和解码的方法，UrlEncode及UrlDecode方法，主要是中文和%开头的互转，代码参考微软实现。例如 AB好 和 AB%E5%A5%BD 互转</item>
	///            <item>HttpServer: 新增委托HandleFileUpload，当客户端上传文件时触发，然后获取到文件名和内容，可以自己进行存储，目前仅支持单个文件上传。</item>
	///            <item>DLT645-2007: 网口串口代码优化，相同的逻辑代码提炼，并增加属性 DLTType，返回枚举：DLT645Type.DLT2007</item>
	///            <item>DLT645With1997: 新增 DLT645/1997 协议的实现，使用ReadDouble来读取相关的数据，例如地址：B6-11 读取电压A数据。</item>
	///            <item>DLT645With1997OverTcp: 新增 DLT645/1997 网口透传实现的版本，使用TCP/IP通信并数据交互，使用方法和 DLT645With1997 一致。</item>
	///            <item>SiemensPPI: 优化西门子PPI协议数据接收时完整性判断，数据一旦接收完成，立即返回。因为在某些特殊情况下，数据接收不完整时会发生异常。</item>
	///            <item>SiemensPPIOverTcp: 接收数据时循环接收，并判断数据完整性，数据一旦接收完成，立即返回，增加循环超时时间，为ReceiveTimeout。</item>
	///            <item>CJT188: 新增CJT188-2004的水表（燃气表）协议，使用ReadDouble 和 ReadStringArray通信，地址示例：90-1F读取当前的累积流量，同时添加网口透传的版本。</item>
	///            <item>FanucSeries0i: fanuc的机床新增属性OperatePath，默认为1，当机床为多路径的时候，可以设置为2，然后对路径2进行相关的操作，例如读取报警，读取程序。</item>
	///            <item>MqttServer: 优化安全性，当没有指定RSA密钥时，针对每个连接的客户端对象，都创建随机的RSA密钥，来提升安全性。</item>
	///            <item>ILogNet: 日志类接口新增属性HourDeviation，如果需要日志记录时进行时间偏移操作，可以设置该属性实现，如果为-8，就是所有时间往前挪8小时。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2022-11-1" version="11.2.1" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>SiemensPPIOverTcp: 修复在通信速率比较慢的情况下，数据包发生多次接收的时候，接收不正确的bug。</item>
	///            <item>SecsValue: 修复在创建None类型的数据时，引发空对象的异常。</item>
	///            <item>SecsHsms: 优化Secs协议的客户端和服务器的心跳接收，修复了和其他客户端测试连接不正常的bug，DEMO界面添加大量的测试信息。</item>
	///            <item>CJT188: CJT188OverTcp 协议新增属性 StationMatch 用来标记是否匹配到站号一致的报文再返回，默认为false，修复读取数据失败时，直接报异常的bug。</item>
	///            <item>SiemensS7Server: 修复西门子虚拟PLC在使用node-red的s7组件访问时频繁在线，掉线的bug，原因来自消息id不匹配。</item>
	///            <item>DLTTransform: 当没有遇到内置转换DLT数据的时候，不再返回失败，返回数据的HEX字符串形式，用来支持扩展的情况读取。</item>
	///            <item>HslCommunication的netstandard版本的库所依赖的System.IO.Port 版本调整为 6.0.0</item>
	///            <item>Demo: 完善TCP/UDP调试窗体，Server端修复选择文本不显示字节长度的bug，修复关闭时连接不断开的bug，界面位置调整优化。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2022-11-18" version="11.2.2" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>SharpList: 修复Add一个新的值的时候，每隔指定长度次数时数据丢失一次的bug。新增LastValue( )方法获取最后一个值。</item>
	///            <item>AllenBradleyNet: ReadBool(string address, short length); 支持读取以int类型为基础的bool数组，支持从中间任意位置开始读，返回任意长度。地址前加i=,例如i=AAA[10]</item>
	///            <item>NetSoftUpdateServer: 修复路径在linux下运行时，路径分隔符识别不正确，导致文件下载失败的bug。</item>
	///            <item>Modbus: modbus的地址支持自定义写入功能码，例如自定义的服务器有个数据区，读使用07功能码，写使用08功能码，那么地址可以表示为 x=7;w=8;100  依然可以支持站号。</item>
	///            <item>CJT188Helper: 修复CJT188协议再读取水表数据的时候，数据位颠倒的bug。</item>
	///            <item>Omron: 在欧姆龙Fins协议里，当读取位数据超过1998时会发生错误，现在根据1998长度自动切割，也就是支持任意长度了。</item>
	///            <item>ReverseBytesTransform: 修复指定DataFormat对象实例化的时候，指定DataFormat无效的bug。</item>
	///            <item>NetworkDoubleBase: 基础的网络设备通信基类支持了使用MQTT中转读取的方式，指定mqtt对象，读Topic，写Topic就可以远程读写PLC信息。可以配合我司的边缘网关系统的设备转MQTT使用。使用手册后面推出。</item>
	///            <item>DLT645With1997: 修复DLT645-2007解析数据异常的bug，修复读取电压倍率不对的bug。</item>
	///            <item>ModbusHelper: 修复汇川PLC读取bool时，当指定MX500.7能读bool值，但是MX501.7 这种地址时，获取不到正确的值的bug。</item>
	///            <item>Authorization: 支持一种在线激活方式，使用指定ip，端口，令牌获取远程激活码来激活的操作，后续扩展临时激活授权测试的功能。</item>
	///            <item>FanucSeries0i: 读取程序的API接口ReadProgram再接收完程序后，再接收一次0x18指令的报文。</item>
	///            <item>OmronHostLinkHelper: 欧姆龙HostLink协议在解析报文的错误码时，当解析失败时，增加错误捕获，并提示原始报文。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2022-12-9" version="11.3.0" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>byte[]: 类型byte[]新增加一个扩展方法，ReverseByWord，直接可以调用按照字反转字节数据信息。</item>
	///            <item>OmronCipNet: 欧姆龙的CIP协议优化，写入数组时，根据地址是否带索引，来自动确认写入的长度信息。</item>
	///            <item>MelsecMcServer: MC协议的虚拟PLC部分，在二进制下支持了随机字读取（0403）和块读取（0406）的功能码。</item>
	///            <item>WebSocketServer: 现在服务端新增属性：TopicWildcard，如果客户端使用通配符订阅数据点。规则和MQTT的通配符一致。</item>
	///            <item>HttpServer: 修复request.ContentType为空的情况下引发的异常，原因来自对文件上传的判断。</item>
	///            <item>SiemensS7Net: 修复西门子PLC在不连接的情况下，直接同步方法读写PLC相关的值的时候，因为pdu初始值不正常导致第一次读写失败的bug。</item>
	///            <item>Demo: Tcp的服务器调试界面里，修复点击发送客户端时，发送的数据不显示的bug。</item>
	///            <item>DLT645With1997OverTcp: 修复使用异步ReadDoubleAsync读取数据的时候，数据结果解析不正确的bug。</item>
	///            <item>OpenProtocolNet: 完善开放以太网协议，并且基本测试通过，支持自定义的功能码读取，支持订阅操作。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2022-12-28" version="11.3.1" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>Dll: 用于激活的类Authorization新增一个方法SetDllContact，用于设置提醒激活的联系方式，可以设置自定义的联系方式。</item>
	///            <item>Demo: 修复安川的MemobusUdpNet的测试界面的点击实例化按钮后，界面还是灰的bug。</item>
	///            <item>AllenBradleyNet: 修复ab-plc的CIP协议，当触发0x64错误码的时候，一直重复读写无法恢复成功的bug，现在会自动重连操作。</item>
	///            <item>SiemensS7Net: 新增对DTL格式的时间数据读写的API接口，使用ReadDTLDataTime和WriteDTLTime来读写操作</item>
	///            <item>LogStatisticsBase: 修复当系统的时间往回调的时候引发异常的bug，原因来自索引偏移位置会变成负数。</item>
	///            <item>websocketServer: Websocket服务器header接收订阅时，支持url转义，以及修复开启通配符模式情况下，消息驻留发送失败的bug。</item>
	///            <item>Demo: 串口调试界面以及网口调试界面，返回的数据支持勾选自动返回，只要在发送框里提前输入数据。</item>
	///            <item>Dlt645: DLT645的地址支持reverse标记，用来支持在某些不标准的协议的设备，数据顺序没有颠倒的情况，例如：reverse=false;02-01-01-00</item>
	///            <item>Inovance: 汇川的AM系列的地址，支持MD的地址格式解析，同时适用于读写操作，MD100=MW200</item>
	///            <item>SiemensS7Helper: 修复了西门子虚拟PLC及SiemensS7Net的写入字符串的同步方法，字符创长度信息和实际不匹配的bug。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2023-2-7" version="11.3.2" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>WebsocketServer: 在发布Topic数据的时候，新增一个API，允许指定reatain属性，如果不指定，则用对象自己的设置。</item>
	///            <item>InovanceHelper:汇川读取byte的方法新增错误异常捕获，返回失败的说明信息。</item>
	///            <item>SiemensS7Server: 修复虚拟服务器在接收远程客户端的写入定时器，计数器时，地址偏移不正确的bug。</item>
	///            <item>modbus: 当调用地址为 x=1;100 进行写入bool数据的时候，修复功能码不会自动修正为正确的默认功能码的bug。适用于 rtu, tcp, rtu overTcp</item>
	///            <item>BeckhoffAdsServer: 修复当远程客户端读取M100.0 的bool类型时，指定长度大于1时，返回数据不正确的bug。</item>
	///            <item>BeckhoffAdsNet: 修复在读写I,Q地址类型时，初始偏移位置不正确的bug，无论是字读写，还是位读写都测试通过。虚拟PLC侧自动识别偏移地址。</item>
	///            <item>DLT645: 修复DLT645的1997及2007版本串口通信时，在某些特殊情况下通信速度慢导致接收数据长度不够引发异常的bug。</item>
	///            <item>MegMeet: 新增麦格米特的PLC的通信类，底层使用Modbus协议，支持串口，网口，支持系列：MC80/MC100/MC200/MC280/MC200E</item>
	///            <item>KeyenceDLEN1: 新增基恩士的以太网模块通信协议实现，主要用于各种传感器的数据读写操作，提供DEMO测试。</item>
	///            <item>NetworkBase: NetworkBase基础通信基类，新增了一些基于SSL加密通信的报文收发的方法支持。</item>
	///            <item>MqttClient: MqttClient支持了使用证书的方式进行通信，设置MqttConnectionOptions.CertificateFile为证书路径即可实现证书加密及校验，已和EMQX测试通过。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2023-2-20" version="11.3.3" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>HslCertificate: 新增自定义的证书类，支持持有者颁发证书，对证书验证签名，例如可以使用证书实现对API接口权限验证，详细查看API文档。</item>
	///            <item>OmronCipNet: 修复异步写入字符串时，编码无法指定ASCII之外的bug，导致无法使用UTF8写入中文。</item>
	///            <item>ModbusTcpServer: 重新增加属性 StationCheck, ，当服务器只有一个站号的时候，设置为True表示校验客户端请求的站号，反之则不校验。</item>
	///            <item>DLT645及DLT645OverTcp: 支持写入字符串的方法，字符串将会转为二进制并且颠倒顺序，写入到仪表里去，写int及double数组也会转字符串数组写入。</item>
	///            <item>WebsocketServer: 服务器初步支持了SSL通信，使用证书通信，需要带私钥的证书才能成功创建，等待进一步的测试。</item>
	///            <item>LogNetBase: 日志获取相关的存储列表的方法中，增加一个错误捕获，在极端情况下可能会因为错误异常。</item>
	///            <item>Authorization: 激活系统的方法新增基于证书激活的方式，具体使用方法参考官网文档。</item>
	///            <item>Demo: Demo主界面的左侧的设备列表信息改为浮动窗体，可以自由隐藏，DEMO也支持了使用证书激活。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2023-3-25" version="11.5.0" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>DLT698: 698协议新增属性UseSecurityResquest，设置是否使用安全模式来通信，默认true，影响范围包括串口，透传，网口类，特殊的仪表需要设置 false。</item>
	///            <item>DLT698: 新增写入时间数据到地址地址的接口，WriteDateTime( string address, DateTime time )，新增698协议的接口 IDlt698</item>
	///            <item>FanucInterfaceNet: Fanuc机器人对象读取机器人数据时，FanucData解析字符串数据的编码可以手动调整指定。</item>
	///            <item>MelsecFxLinksHelper: 三菱的计算机链接协议里，如果传入的报文以ENQ开头的，则认为传入原始包，不进行封包处理。</item>
	///            <item>OmronFinsAddress: 欧姆龙的Fins协议支持CF地址，可以读取脉冲信号，地址一秒脉冲地址示例：CF1.2  新增IHostLinkCMode, IOmronFins接口。</item>
	///            <item>新增三菱的编程口接口 IMelsecFxSerial，新增西门子PPI接口 ISiemensPPI，永宏编程提炼接口 IFatekProgram</item>
	///            <item>AllenBradleyNet: 批量读取地址数组的方法，传入长度数组参数由 int 改为 ushort 类型，所有的CIP协议实现了统一的IReadWriteCip接口。（可能不兼容）</item>
	///            <item>FanucSeries0i: ReadProgram方法取消再次接收0x18的命令，因为在 32i 的机子上是会触发超时接收。</item>
	///            <item>YamahaRCX: 雅马哈机器人新增 JogXY 接口，使用微动的方式操作六轴的动作信息，在demo界面上支持JOG的测试。</item>
	///            <item>AllenBradleyHelper: 修复CIP协议创建命令为0x4D写入数据报文时，数据长度过大导致溢出的bug，在解析返回结果数据时增加错误捕获操作。</item>
	///            <item>Modbus: 所有Modbus的客户端以及RTU主站类，接口IModbus新增使用功能码0x17的一条报文实现读写方法ReadWrite，虚拟服务器支持该功能码。</item>
	///            <item>SiemensS7Net: Read( string[] address, ushort[] length )方法不仅仅根据19个长度切割，还根据长度之和是否满足200字节切割。</item>
	///            <item>FinsTcp: Fins协议读取bool时，发现返回数据长度小于指定长度时，自动偏移地址并读取剩余数据，然后返回全部给调用者。</item>
	///            <item>SimpleHybirdLock: 通信的混合锁新增当前锁对象等待的计数属性：LockingTick，可以用来监视锁对象的竞争情况。</item>
	///            <item>Demo: TCP转TCP的支持二进制和ASCII格式的转换显示，完善MegMeet，Lsis的添加，批量读取控件增加提示功能。</item>
	///            <item>HslCommunication: 大部分PLC通信的消息解析代码增加合法性验证，并且增加异常捕获，防止及其特殊的情况导致系统奔溃。</item>
	///            <item>Demo: Demo程序大升级，测试界面支持定时读取，定时写入，写入支持动态数据变化。批量读取，报文读取，特殊功能测试优化，更加便捷。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2023-4-14" version="11.5.1" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>MelsecFxSerial: MelsecFxSerial的报文日志记录格式调整为ASCII格式，当设备的日志实例化时，就会自动当前的通信日志。</item>
	///            <item>IReadWriteMc,IReadWriteA3C: 新增属性EnableWriteBitToWordRegister，当开启后支持往D寄存器写入位数据，实现方式为读字，修改位，写字操作，具体风险查看该属性说明。</item>
	///            <item>KeyenceMcNet, KeyenceMcAsciiNet: 基恩士的MC协议的通信，修复ZR地址因为地址进制导致读取不到正确地址的bug，现在使用16进制地址表示。</item>
	///            <item>KeyenceMcNet, KeyenceMcAsciiNet: 地址兼容了基恩士自己的地址格式，可以直接输入 DM100, FM100, EM100, MR015, 或是直接 MR1.2 等</item>
	///            <item>HslHelper: 新增属性LockLimit表示竞争锁上限(默认1000)，NetworkDoubleBase,NetworkUdpBase,SerialBase检测当锁竞争达LockLimit次时，直接返回失败，不在增加竞争。</item>
	///            <item>HslTimeOut: 检查socket超时部分的功能代码，当处于linux系统下时，在确认超时close之前，增加一个 Disconnect操作，解决特殊情况close会卡时间的bug。</item>
	///            <item>HslHelper: HslHelper新增属性UseAsyncLock, 标记本通信库的单个通信对象在异步通信的时候是否使用异步锁，默认True, 当使用控制台或是纯后台线程采集时，配置 False 更好。</item>
	///            <item>Modbus: 在写入单个的short及ushort值时，默认使用06功能码，现在支持如果在地址里使用w标记，例如 w=16;100  时，写入时报文就使用16功能码。</item>
	///            <item>TcpForward: 新增属性CacheSize，表示中转时候的数据缓存大小，默认是2048</item>
	///            <item>Net2.0, 3.5, Standard项目添加缺失的类：VigorServer, OmronHostLinkCModeServer, MelsecFxLinksServer, KeyenceDLEN1</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2023-4-19" version="11.5.2" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>OmronCipNet: 修复写入单个Byte数据对象，或是奇数长度的byte[]时，plc报错31错误码的异常，现在可以正常的写入了。</item>
	///            <item>SiemensS7Net: 西门子批量读取地址数组的方法里，原先总长度按照200字节切割变更为按照自动获取的pdu长度进行切割。</item>
	///            <item>MqttClient: Mqtt客户端的连接类MqttConnectionOptions新增属性UseSSL指示是否开始SSL/TLS加密功能，验证证书时修复服务器名称输入错误导致有些服务器连接失败的bug。</item>
	///            <item>NetworkUdpBase: 优化UDP通信基类，增加设置获取PipeSocket方法, 支持设置多个端口，因网络读取失败时自动切换另一个端口。</item>
	///            <item>Modbus: ModbusRtu设备在提取接收到的报文时，校验数据长度并且如果发现长度太长，则按照标准报文长度切割，即使后面跟0xFF扰码也能正确读取</item>
	///            <item>Memobus, DigitronCPL, MegMeet, Delta 相关的通信类，补全缺失的 MPRC 接口注册功能。</item>
	///            <item>Demo: 三菱相关的PLC测试界面在连接PLC失败的时候，原先只提示连接失败，现在提示更加详细的信息。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2023-5-15" version="11.5.3" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>NetworkBase: 在ReceiveByMessage方法里，新增判断消息报文剩余长度小于0时，直接返回头报文信息，防止在万一接收到错乱数据导致小于0引发bug的异常。</item>
	///            <item>FFTFilter: 傅立叶变化的Filter滤波方法，修复当数据有负数的时候，只能得到正数结果的bug，现在可以还原出带有负数情况的波形图。</item>
	///            <item>SiemensS7Server: SiemensS7Server增加在握手报文处理时，对握手报文合法性的检查操作，防止收到一些奇怪数据时导致奔溃。</item>
	///            <item>DLT698: 修复在UseSecurityResquest为默认True情况下，APDU指令创建不正确的bug，涵盖DLT698, DLT698TcpNet, DLT698OverTcp类。</item>
	///            <item>FanucSeries0i: 新增根据字符串程序名读取程序的方法接口ReadProgram( string program, string path = "" )，用来支持读取非数字名称的程序号。</item>
	///            <item>Toyota: 新增丰田工机的计算机链接协议实现，ToyoPuc 类，通过2PORT-EFR模块实现对PLC的读写数据操作，同时增加ToyoPucServer虚拟PLC进行测试。</item>
	///            <item>DLT698: 修复698的协议中的CheckResponse，当请求类型不支持时，引发空对象的异常。</item>
	///            <item>SerialBase: 间歇时间SleepTime允许设置0及小于0，小于0就是不进行任何休眠，三菱编程口MelsecFxSerial增加对消息完整长度的校验机制。</item>
	///            <item>OmronCipServer: 新增欧姆龙自身的CIP服务器，支持客户端使用OmronCipNet类和OmronConnectedCipNet进行通信，支持普通点位，数组，字符串的读写操作。</item>
	///            <item>CipServer: AllenBradleyServer和OmronCipServer支持所有的标签数据存储到文件和从文件加载的操作，demo里服务器的存储加载支持路径选择。</item>
	///            <item>MelsecA1EServer: 新增模拟PLC的实际情况，针对bool读取长度超过256返回错误码，针对字单位读取长度超过64即返回错误。</item>
	///            <item>MelsecA1E: MelsecA1ENet及MelsecA1EAsciiNet类修复读取bool数组时长度超过256报错的bug，MelsecA1EServer完善读取bool超过256返回错误。</item>
	///            <item>Mewtocol: 松下的Mewtocol的串口及网口类对象新增支持读取PLC型号的方法ReadPlcType，虚拟服务器也支持了该功能码。</item>
	///            <item>SerialBase, NetworkDoubleBase: 优化通信代码，针对Thread.Sleep方法也进行错误捕获，防止可能的异常。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2023-6-25" version="11.6.0" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>DLT645: DLT645-2007 新增对设备返回的报文进行和校验码校验的步骤，如果校验失败，返回错误信息。</item>
	///            <item>MelsecA1EAsciiNet: 修复在字读取长度超过64字时，或是bool读取超过256位时，读取结果不正确的bug。现在支持读取任意的长度数据。</item>
	///            <item>AllenBradleyServe: 修复AllenBradleyServer及OmronCipServer在配置了写入时创建新标签后，写入字符串数据仍然报错，提示标签不存在的bug。</item>
	///            <item>IDlt645: DLT645接口新增Password属性及OpCode，方便在通信时动态修改这两个参数，主要针对DLT645/2007的,对于 DLT645/1997 协议来说无效</item>
	///            <item>MelsecMcServer: 修复MC虚拟服务器在ASCII模式下，写入D之外的寄存器，但是仍然写入D寄存器的bug，影响范围包括A3CServer。</item>
	///            <item>Melsec: 三菱的MC协议开放属性 PlcNumner，可以自由的设置，表示PLC编号。影响范围:MelsecMcNet, MelsecMcAsciiNet, MelsecMcUdp, MelsecMcAsciiUdp </item>
	///            <item>OmronFinsServer: 欧姆龙的FinTcp协议的虚拟PLC支持了CF数据地址，无论是服务器上，还是通过客户端来读写CF数据都可以正确的读取。</item>
	///            <item>FanucSeries0i: 客户端demo修复删除程序，只能删除主目录的程序号的bug，现在可以删除任意指定路径的程序文件信息。</item>
	///            <item>WebsocketServer: websocket服务器运行时，当客户端使用火狐浏览器连接服务器时，修复检测websocket连接失败的bug。</item>
	///            <item>ToyoPuc: 修复丰田工机PLC中，当地址是 U0 及 H0 的时候，地址输入解析不正确的bug，原先的bug是忽略第一个数字。</item>
	///            <item>AllenBradleyServer: CIP虚拟服务器优化通信的细节，正确的设置了基于连接模式下的各种连接ID信息，当PLC使用MSG模块读写虚拟PLC时也顺利通过。</item>
	///            <item>Demo: HslCommunicationDemo程序几乎所有的设备测试界面的线程压力测试界面单独拎出来，显示单次通信的平均耗时。</item>
	///            <item>Demo: HslCommunicationDemo程序几乎所有的设备测试界面增加设备的地址示例说明，带地址示例，含义，部分PLC提供地址范围说明。</item>
	///            <item>Demo: HslCommunicationDemo程序几乎所有的设备测试界面增加数据点位表的功能，可以配置点位表，保存，加载，然后多个点位同时刷新。</item>
	///            <item>.net standard 框架的dll依赖的 System.IO.Ports 版本由 6.0.0 升级到 7.0.0。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2023-7-18" version="11.6.1" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>Demo: 保存设备的连接参数及点位数据的时候，支持了设置密码，设置密码后，加载该设备参数时，必须输入正确的密码才能打开。</item>
	///            <item>Demo: 串口调试界面，TCP调试界面，TCP Server调试界面大幅度的调整，优化，更好的区分收发数据，以及数据长度信息。</item>
	///            <item>Demo: 在demo界面的曲线控件界面，从实时曲线修改为历史曲线控件，新增设置颜色，曲线样式，显示最大值，最小值信息，自动滚动条往右。</item>
	///            <item>Demo: 调试的程序在连接成功，或是打开串口之后，直接在测试界面上显示当前的连接类的示例代码，初始化参数和demo界面保持一致，方便直接复制。</item>
	///            <item>ModbusTcpServer: modbus虚拟plc当属性UseModbusRtuOverTcp = true时，同时支持rtu over tcp报文及 ascii over tcp报文，并对 ascii 报文做了更加严格的区分，防止弄乱。</item>
	///            <item>ModbusRtuOverTcp: 通过调整底层基类的读取空消息的报文时，可能引发数据不完整的bug，针对部分回数据断断续续的设备，现在可以读取正确的值信息。</item>
	///            <item>AllenBradleyNet: 优化CIP协议的读写字符串功能，对长度判断更加的完善，当写入字符串时，可以指定额外的type值，例如地址为"type=0xD1;AA"，影响范围：AllenBradleyMicroCip</item>
	///            <item>HslTimeOut: 优化超时的代码信息，单独添加超时的日志信息，如需记录，可以实例化 HslTimeOut.TimeoutLogNet 属性，并且可以获取到当前所有的超时判断次数 HslTimeOut.TimeoutDealCount</item>
	///            <item>SecsHsms: 优化secs的代码，增加在解析secs消息的时候，对异常错误的消息捕获并记录日志，demo界面优化显示信息，并提供代码示例。</item>
	///            <item>AllenBradleyNet: 写入字符串的时候，如果类型为 DA 的情况，则使用另外的写入方式，在microcip上测试成功。</item>
	///            <item>PanasonicHelper: 修复松下的mewtocol协议使用字的方式写入数据到Y,R,L地址时，引发0x41错误码的bug，原因来自结尾地址偏移异常。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2023-8-13" version="11.6.2" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>YRCHighEthernet: 新增ReadManagementTimeSpan接口，用来读取持续的时间，返回的是字符串信息。完善demo界面，提供示例代码信息。</item>
	///            <item>NetworkDoubleBase: 新增一个发送前的报文头设置属性 SendBeforeHex，当使用lora中转通信时，可以设置为lora站号，例如00 00 00 02</item>
	///            <item>ModbusUdpServer: 新增基于UDP通信的ModbusUdpServer虚拟服务器类，在demo界面上的ModbusTcpServer启动时，可选TCP还是UDP功能，方便测试。</item>
	///            <item>MelsecFxLinksOverTcp: 修复属性 WaittingTime 可以设置0xF 以上值的bug，实际这个值不允许设置 0x0f 以上。</item>
	///            <item>XinJEServer: 当服务器站号不一致的时候，并且服务器设置检查站号的情况，直接返回站号不一致的错误消息。</item>
	///            <item>KeyenceNanoServer: 基恩士的上位链路虚拟PLC的字符串是否反转默认设置 true, 如果需要，手动修改server.ByteTransform.IsStringReverseByteWord</item>
	///            <item>FanucSeries0i: 新增方法 ReadAxisNames 读取轴名称以及 ReadSpindleNames读取主轴名称。</item>
	///            <item>IEC104: 添加总召唤方法TotalSubscriptions，优化值解析的过程，Demo界面进行了优化，显示报文的时候，显示其对应的功能说明，api文档新增示例。</item>
	///            <item>AllenBradleyNet: 修复当PLC对象修改了ByteTransform.DataFormat的值后，导致读取PLC时提示会话ID不一致的bug。</item>
	///            <item>DEMO: 在TCP Server调试的界面上，增加保存当前界面的配置参数的功能，连带配置的报文列表也一并存储。</item>
	///            <item>DEMO: 修复DebugControl控件在窗体拖动时，重复触发发送数据按钮的bug，多来回拖动后将会导致程序异常卡顿。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2023-9-25" version="11.6.3" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>ModbusTcpServer: 当客户端使用了0xff的站号（10进制为255）的时候，将自动忽略对站号的校验，直接可以进行读写操作。</item>
	///            <item>SiemensS7Net: 新增强制位ForceBool方法，以及取消强制CancelAllForce方法，目前针对200smart测试通过，支持对I,Q的点位强制操作。</item>
	///            <item>OmronFinsServer: 优化返回的报文信息，方便wireshark软件抓包进行分析操作，识别到正确的Fins协议。</item>
	///            <item>SiemensS7Net: 西门子s7协议的报文中，消息号id修改为自增，修复了在某些特殊的200smart型号读取超时的异常。</item>
	///            <item>OmronCipNet, OmronConnectedCipNet: 读取字符串的方法，在解析字符串数据内容的时候，增加错误捕获，并返回数据的二进制内容。</item>
	///            <item>NetworkDeviceBase: 写入数据的几个类型的重载方法，也更改为虚方法，支持子类重写操作。</item>
	///            <item>SiemensS7Net: 新增方法  Write( string[] address, List&lt;byte[]&gt; data )支持同时写入多个数据块的地址数据，SiemensS7Server也支持多个数据块的写入功能。</item>
	///            <item>Melsec: 三菱的协议MelsecA1ENet，MelsecA1EAsciiNet, MelsecFxLinks(含串口及以太网), MelsecA3CNet(含串口及以太网), 读取bool时支持字地址的位，例如 D100.0</item>
	///            <item>CJT188Helper: 水表协议188修复再某些特殊的情况下解析结算累积量异常时，导致读取当前累积量也失败的bug。</item>
	///            <item>OmronFinsNet: 新增属性 ReceiveUntilEmpty, 默认false，设置true后可以在一些及其特殊的场景里防止数据错误的情况。</item>
	///            <item>LogNetManagment: 日志管理对象的一个静态方法优化，增加错误捕获，防止极小概率的意外情况。</item>
	///            <item>SiemensS7Plus: 新增基于s7plus协议的通信对象，使用SSL/TLS加密通信，支持读写数据，不需要开启put/get，支持遍历点位，仅支持.net framework平台，还需要依赖两个openssl的C++库。</item>
	///            <item>Demo: 支持将PLC数据定时读取，然后导出到单个文件。然后可以在虚拟服务器上进行加载文件，然后定时写入，可以本地模拟现场设备数据。</item>
	///            <item>Demo: 原始字节的批量读取功能显示效果，新增整型显示，当切换hex，ascii，整型的时候，自动更新显示数据，更加便捷。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2023-10-26" version="11.6.4" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>ByteTransformHelper: 一个转换的静态辅助方法GetSuccessResultFromOther增加异常的捕获，防止在一些极其特殊的情况下导致异常然后奔溃。</item>
	///            <item>IReadWriteMc: MC协议的接口新增属性TargetIOStation，用来表示目标模块IO编号，默认0x03FF, 表示管理的CPU，当访问多CPU时，需要设置本值。</item>
	///            <item>ABBWebApiClient: abb机器人的类新增接口GetUserValue，用来获取用户自定义的一些数据内容，返回double[]，demo增加测试功能。</item>
	///            <item>ModbusRtu: rtu报文的解析新增加对头部干扰码的支持，就算报文前跟了一个字节的无用报文，现在也可以正确的解析，也适用于rtuovertcp.</item>
	///            <item>MqttServer: 文件引擎新删除整个目录的接口方法，重命名目录的接口方法，MqttSyncClient代码优化，并且添加配套的接口实现。</item>
	///            <item>HttpServer: 新增Action类型属性ApiCalledAction，当一个接口调用完之后触发，可以获得该接口一些调用信息，方便二次处理。</item>
	///            <item>BeckhoffAdsNet: 新增加ReadStruct&lt;T&gt;( string address )方法及WriteStruct&lt;T&gt;( string address, T value )针对struct类型的对象读写。</item>
	///            <item>Demo: 在所有的PLC设备的读写测试的控件里，在点击了byte类型的读取时，支持了设置长度大于1的情况下，批量读取了byte数据，显示为十进制数据。</item>
	///            <item>Demo: DEMO中的所有的PLC测试界面优化，在没有连接PLC的情况下，也可以切换选项卡查看地址示例。</item>
	///            <item>Nuget: netstandard框架的项目依赖的System.IO.Ports组件版本从7.0降级为6.0，原因是7.0在某些特殊的ARM硬件里存在问题。</item>
	///            <item>HslHelper: 方法CalculateBitStartIndex优化匹配代码，使用了正则表达式进行判断，提升程序速度。</item>
	///            <item>SerialBase: 串口基类，网口基类的一点点代码优化，针对及其特殊情况的一点异常捕获。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2023-11-10" version="11.7.0" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>SiemensS7Net: 西门子的用于计数的消息id在重连plc的时候，自动重置为1，然后重新开始计数。</item>
	///            <item>DLT645: DLT645协议新增对返回的站号检查是否一致的功能，如果刚好站号倒序，则也认为一致。优化消息接收，即使报文前面跟了几个字节的无用信息。</item>
	///            <item>ModbusRtuOverTcp: 优化通信机制，对于单次接收的报文支持了更多的容错，如果CRC校验失败，不再关闭当前的socket连接操作。</item>
	///            <item>MqttServer: 当客户端订阅一个主题之后，如果这个主题属于消息驻留的主题，给当前客户端返回的消息里增加Retain=true标记，方便客户端标记。</item>
	///            <item>MqttServer: 注册MRPC的接口时，如果之前已经有相关的接口注册，现在改为直接覆盖，然后变量参数名自动适配 value 和 values，这两个没找到就相互替换名称。</item>
	///            <item>DLT645Server: 新增DLT645-2007协议虚拟仪表实现，支持一些常见的数据的简单测试，例如电压，电流，频率，时间等信息。</item>
	///            <item>Newtonsoft.Json, NET20项目，NET35项目，NET451项目对JSON库的引用调整SpecificVersion为 False </item>
	///            <item>Demo: 地址示例的表格支持右键弹出上下文菜单，点击复制地址，即可复制当前的地址信息。</item>
	///            <item>Demo: 数据点位表控件DataTableControl修复虚拟服务器不能刷新数据的bug，新增了双击值表格写入数据的功能。</item>
	///            <item>Demo: 修复DEMO服务器的部分ip地址获取网址解析不出数据的bug，所有的襄阳地址均改为襄樊，方便旧版地址显示。</item>
	///            <item>MqttClient: 不兼容更新，收到MQTT的事件签名OnMqttMessageReceived修改为MqttMessageReceiveDelegate( MqttClient client, MqttApplicationMessage message );支持携带Retain信息。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2024-01-26" version="11.8.0" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>SiemensS7Server: 西门子PLC的虚拟服务器，当客户端连接的时候握手报文也进行输出到日志上面。</item>
	///            <item>OpenProtocolNet: 新增属性RevisonOnConnected，用来设置连接初始化时的MID0001的revison信息，如果小于0，初始化连接时则不使用MID0001</item>
	///            <item>AdsHelper: 倍福的ADS协议里的部分错误码提示的消息附带一些建议的解决方法，可以快速查找问题。</item>
	///            <item>SiemensS7Helper: s7协议里如果执行写入wstring类型的字符串时，当检测到字符串最大长度为0时，自动分配254长度的字符串。</item>
	///            <item>MC1EServer: MC协议的虚拟服务器及MC1EServer的虚拟服务器支持了S0,F0 地址，也支持远程客户端读写该地址。</item>
	///            <item>MqttRpc: 在Mqtt服务器注册了RPC服务器接口后，当注册的自定义的接口方法本身发生异常时，增加提供原始异常相关的本文及堆栈信息。</item>
	///            <item>SiemensS7Net: S7协议新增对地址 SM0.0 的支持，在200系列上测试成功。 新增 PIW0,  PQW0 外设地址，在S7-300上测试成功。</item>
	///            <item>HslReflectionHelper: 解析HslStructAttribute特性时，类型为字符串时，修复编码为GB2312，但是编码设置异常的bug，现在自动忽略大小写。</item>
	///            <item>OmronFinsTcpServer: 虚拟PLC支持了CF地址及DR地址类型的读写操作。</item>
	///            <item>OmronCipServer: 当服务器创建了bool[]数组的标签时，修复客户端读写该标签不一致的bug。</item>
	///            <item>IEC104: 104规约总召唤方法的传送原因默认值改为06，新增属性Station表示单元公共地址，并在DEMO上可设置。</item>
	///            <item>ABBWebApiClient: abb机器人代码优化精简，部分接口自动解析更多的数据，demo测试界面显示优化，同时显示解析结果和html文本。</item>
	///            <item>OmronFinsUdp: 报文里的SID字段每次通信将自增1，然后校验返回报文是否一致，虚拟服务器适配客户端。</item>
	///            <item>OmronFinsNetHelper: Fins协议针对EndCode结束码判断增加网络异常标记位判断，直接返回读写失败及错误消息。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2024-01-29" version="11.8.1" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>MelsecMcServer: 修复MC虚拟服务器当远程客户端读取D，W，R，ZR字地址数据时回复不支持的bug，影响范围包括Udp,A1E,A3C服务器。</item>
	///            <item>NetworkDataServerBase: 修复虚拟服务器基类启动串口接收后，触发OnDataReceived事件的报文不是接收到客户端报文的bug。</item>
	///            <item>NetworkDataServerBase: 修复虚拟PLC服务器基类的事件OnDataSend没有触发的bug，就是服务器回发客户端数据的事件。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2024-02-14" version="11.8.2" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>OmronFinsNetHelper: 欧姆龙的Fins协议的从错误码获取消息文本的方法修复部分错误码获取文本不匹配的bug。</item>
	///            <item>MqttServer: MQTT服务器针对每个会话的客户端(针对客户端启用了RSA加密的情况)使用临时生成的AES随机密钥，防止AES密钥被伪装客户端获取。</item>
	///            <item>NetworkDoubleBase类的ConnectClose方法以及ConnectCloseAsync方法取消了锁操作，一般建议系统退出时再调用一次，demo上接收超时时可以立即关闭连接。</item>
	///            <item>NetworkBase类以及NetworkDoubleBase类所有的异步操作方法添加.ConfigureAwait( false )操作，防止在部分特殊的情况下使用异步导致死锁卡死UI</item>
	///            <item>MqttServer: 支持MqttSyncClient客户端获取服务器当前的所有的所有在线的会话信息，并增加发布的topic统计功能，也就是客户端可以远程查看服务器在线会话，需要开发者权限帐户。</item>
	///            <item>DEMO MqttServer服务器的测试界面显示的内容优化，显示了在线客户端的详细信息，显示了客户端发布的主题的基本信息。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2024-04-28" version="12.0.0" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>Controls: 删除controls控件，删除图标资源，FormSupport窗体移动到DEMO，FormPortraitSelect移动到hslcontrols组件，本库更加纯粹的负责通信及算法。</item>
	///            <item>SharpList: 更改锁的机制为lock，优化了代码注释以及部分代码逻辑，提升数组长度比较大的时候的性能，提升批量增加数据时候的性能。</item>
	///            <item>NetSoftUpdateServer: 用于程序升级的服务器修复当传送大文件（120M以上时）传送到一半会卡住，传送非常慢的bug。</item>
	///            <item>Modbus: 修复ModbusTcp协议当设备有前置干扰码但是数据长度判断不正确的bug，ModbusRtu新增站号不匹配时持续接收匹配站号的数据。</item>
	///            <item>SecsMessage: 修复secs消息解析的时候，屏蔽了function字段的最高位的bug，该bug会导致大于127的功能码解析异常，现在可以正确的识别。</item>
	///            <item>FormLogNetView: 日志显示的窗体新增带日志路径的构造函数，当指定路径后，显示页面时立即显示日志内容及统计信息。</item>
	///            <item>OmronFinsTcp: 增加对属性SID信息功能，每次请求的时候进行自增，然后校验PLC返回的数值信息，直到接收到一致为止。</item>
	///            <item>ABBWebApiClient: GetJointTarget方法新增mechunit参数，可以指定不同单元。新增GetAnIOSignal方法。</item>
	///            <item>CommunicationServer: 新增服务器类，可以开启TCP,UDP,SerialPort服务器，绑定事件即可接收到客户端发送的数据。</item>
	///            <item>FujiCommandSettingType: 修复写入数据到plc，提示写入失败但是实际成功的bug; 修复西门子PPI虚拟服务器对写入结果的报文返回不正确的bug。</item>
	///            <item>SecsHsms: 针对SendByCommand方法，如果通信异常，则自动连接操作，也就是所有的发送指令都会将管道标记为异常，下次发送时进行重连。</item>
	///            <item>DLT645Server: 修复主站读取时站号不一致时提示校验失败的bug，修复修改站号命令失败的bug，新增属性站号是否校验。</item>
	///            <item>HslHelper: 修复GetIpAddressFromInput方法输入ipv6地址时会导致解析失败的bug，现在同时支持输入ipv4地址，ipv6地址，网址。</item>
	///            <item>Modbus: 新增属性EnableWriteMaskCode，默认支持掩码功能码，如果发现设备不支持，会自动切换false，使用读字修改位写字的方式来实现写位到字寄存器。</item>
	///            <item>ModbusTcpServer: 新增自由控制是否支持掩码功能码; 台达，麦格米特，汇川，信捷的modbus类地址功能优化，并支持了部分字寄存器的写位操作。</item>
	///            <item>ByteTransform: ByteTransformBase基类删除，字节转换类优化，两个字节的整型short,ushort将根据ABCD,CDAB时使用大端，其他情况使用小端转换。</item>
	///            <item>AllenBradleyNet: 地址支持了x=0x52;A1 这种携带52功能码直接使用片段读写的方式，还支持了class=0x6b;0xf68f这种符号实例ID地址，服务器增加配合，修复相关bug。</item>
	///            <item>LogNet: 存储日志因为文件占用等问题发生异常的时候，不再记录存储失败的信息，存储多次才成功的日志加入[Retry:3]标志，相关代码精简优化。</item>
	///            <item>SecsValue: 新增加一个从string[]对象初始化的构造函数，直接生成一个list对象，包含多个字符串。</item>
	///            <item>DLT698: DLT698协议新增客户端地址CA属性，可以配置更改，代码优化，修复读取电能量数据的扩展参数属性时，缩小倍数不正确的bug。</item>
	///            <item>IOmronFins: FinsTcp,finsUdp,hostlink协议新增属性PlcType，可选CS/CJ 和 CV系列，在CV系列地址码略有不同，fins协议支持ReadCpuTime读取PLC时间。支持了地址 E100 当前数据区</item>
	///            <item>BeckhoffAdsNet: 倍福后台接收数据的地方针对设备通知消息（08功能码）的情况，进行数据屏蔽操作，demo添加ig=0xF030地址格式的示例。</item>
	///            <item>KeyenceMcNet: 修复基恩士MC协议在读取R100.5， MR100.5，LR100.5，CR100.5 地址格式转化真实位地址时地址有偏差的bug。</item>
	///            <item>WebSocketClient: 新增方法 UseSSL( string certificateFile )支持开启证书模式，可以连接 wss 的远程服务器。WebSocketServer也做了代码优化。</item>
	///            <item>V12: 通信类继承体系重新设计, BinaryCommunication(二进制通信)-&gt;DeviceCommunication(设备通信)-&gt;TCP,UDP,串口通信-&gt;每个设备的通信。</item>
	///            <item>V12: 重新设计通信的管道，CommunicationPipe(通信管道)-&gt;TCP,UDP,串口,MQTT管道，前者的二进制通信类，可以随意的设置不同的管道，具体参考手册。</item>
	///            <item>V12: 重新设计所有的虚拟PLC类，DeviceCommunication(设备通信)-&gt;DeviceServer(服务器)-&gt;各个PLC类，同时支持开启TCP，UDP，串口从站的功能。</item>
	///            <item>Demo: 串口网口的收发数据的控件，为了方便模拟设备，输入框支持分批并且延时发送，单独一行&lt;sleep=100&gt;即可，数字100表示休眠的时间，可以修改其他值。</item>
	///            <item>Demo: 修复demo的PLC测试界面里，定时读取的时候，PLC发生掉线后不停弹窗的bug，现在代码进行了优化精简。</item>
	///            <item>请注意，本次升级可能会造成不兼容的情况，具体需要看使用了哪部分的功能，如果简单的使用PLC类例如ModbusTcpNet可以兼容升级，如果使用了基类NetworkDeviceBase，需要替换为DeviceCommunication。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2024-05-24" version="12.0.1" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>MqttServer: 当启动文件服务器时，客户端进行文件上传，下载，删除触发的OnFileChangedEvent事件里添加一个属性：映射文件名称。</item>
	///            <item>PipeSslNet: 新增基于 SSL/TLS 的管道信息类 PipeSslNet, 从PipeTcpNet类继承实现，可以配合 HslCommunication 自身的虚拟服务器实现加密通信，防止抓包。</item>
	///            <item>CommunicationTcpServer: TCP服务器基类代码优化，添加对SSL/TLS的支持，签名修改：ThreadPoolLogin( PipeTcpNet pipe, IPEndPoint endPoint ) 这里如有使用可能不兼容升级。</item>
	///            <item>ModbusTcpServer: 修复当DataFormat配置DCAB时，解析客户端的报文的时候，导致地址及长度信息高地位弄反的bug。</item>
	///            <item>PipeSerialPort: 串口管道新增属性DtrEnable控制，表示串行通信中是否启用数据终端就绪 (Drt) 信号</item>
	///            <item>WebSocketHelper: websocket消息接收使用了管道的实现，删除了原先部分多余的代码，mqtt管道公开一些额外的参数信息。</item>
	///            <item>DeviceSerialPort: 当串口管道调用关闭方法Close的时候，修复管道被重新指定其他非串口管道时，连接不会关闭的bug。</item>
	///            <item>DeviceCommunication: 设备类基类重新添加Dispose接口，然后调用管道的Dispose, 方便代码升级考虑兼容性。</item>
	///            <item>BeckhoffAdsNet: 修复管道配置为SSL及自动获取AMS的情况下，获取ams不使用ssl的bug。</item>
	///            <item>DeviceTcpNet: 重新添加回方法SetPersistentConnection()并标记弃用的状态，方便部分用户升级的时候可以保持向后兼容。</item>
	///            <item>DLT645-2007, DLT645-1997, DLT698添加无参的构造方法，这样DEMO生成的示例代码就可以复制使用，然后优化DEMO上的界面。</item>
	///            <item>MelsecMcServer: 三菱的虚拟服务器针对字地址支持了bool读写操作，也就是ReadBool("D100.1")，写入也是一样。</item>
	///            <item>HttpServer: 注册接口的时候，支持输入参数HttpListenerRequest request时，自动传入客户端的请求头，方便进行二次分析处理。</item>
	///            <item>Wecon: demo新增维控PLC的测试界面，如何实例化相关的对象及支持的地址列表参考demo界面上的地址示例及代码示例。</item>
	///            <item>KeyenceNanoServer: 针对客户端单次读写的数据长度从256，提升到1000长度，因为上位链路协议在某些型号里是1000长度的。</item>
	///            <item>Omron: 修复欧姆龙CV系列的情况下，针对CIO数据区执行bool写入操作异常的bug，原因来自PLC不支持bool的写入CIO功能码，使用读字修改位写字来实现。</item>
	///            <item>Inovance: 修复了Modbus协议地址映射后，针对写入字地址的位情况下可能引发了bug，修复汇川的AM系列读写QX，MX位不匹配的bug。</item>
	///            <item>PipeSerialPort: 优化串口管道的数据接收机制，当定义了报文消息的完整性校验对象后，就不再以收到空数据为终结点，如果数据不完整，就一直接收到超时为止。</item>
	///            <item>ModbusRtu: 修复当设备的数据分段延时返回的时候，设备数据的内容刚好符合一些错误规律的时候，会直接返回部分的报文给调用者，然后判断出 crc异常</item>
	///            <item>MqttClient: 修复调用ConnectServerAsync方法连接的情况下触发的OnClientConnected事件中，获取IsConnected属性还是未连接的bug。</item>
	///            <item>LSis: 删除原来按照XGB,XGK这种命名方式，改为了LsCnet, LSFastEnet类，具体类型的实例化，可以参考demo程序生成的代码示例。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2024-6-3" version="12.0.2" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>OmronFinsServer: 客户端握手报文返回时的命令码修改为1，因为在某些情况下第三方客户端会连接失败</item>
	///            <item>CommunicationServer: 新增管道上线事件OnClientOnline，管道下线的事件OnClientOffline</item>
	///            <item>PipeSerialPort: 修复串口管道当设备方一直不间断发送数据的情况下导致始终不引发超时的bug，影响范围包括所有的串口类设备。</item>
	///            <item>Lsis: lsis的代码优化，支持了DB100这种字节地址，支持直接读short，int多字节数据，修复地址转换异常的bug。</item>
	///            <item>PanasonicMewtocol: 修复日志记录的时候不按照ASCII格式记录的bug。</item>
	///            <item>Demo: 服务器端新增数据模拟的界面，可以使用表达式来动态执行脚本，方便生成一个特殊变化的曲线。</item>
	///            <item>Modbus: 修复Modbus协议在DataFormat为 BADC 及 DCBA 的情况下，写入short及ushort字节不发生转换的bug，影响范围为所有modbus。</item>
	///            <item>FanucSeries0i: 初步添加读取诊断信息方法ReadDiagnoss( int number, int length, int axis )，demo上可以直接测试，欢迎反馈信息。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2024-7-10" version="12.0.3" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>MqttServer: 修复在客户端的掉线事件里如果调用RemoveAndCloseSession移除会话导致内存堆栈溢出的bug，虽然一般都不会这么写代码。</item>
	///            <item>MqttSession: 属性DeveloperPermissions默认值修改为false，在服务器上会话登录后admin用户名默认设置为Ture，也可以自定义更改。</item>
	///            <item>NetSupport: SocketSend及SocketSendAsync方法增加对socket空的校验，防止部分plc实例化后直接调用close直接报异常的bug。</item>
	///            <item>HttpServer: 修改HandleRequest方法及属性HandleRequestFunc返回方法为object, 支持返回string及 byte[]，可以用来传文件，此更新为兼容的。</item>
	///            <item>MemobusTcpNet: 安川的协议地址支持了M100,G100,I100,Q100,S100这种地址，支持超大地址，支持对位原生操作。</item>
	///            <item>NetworkConnectedCip: 两个连接时的会话ID属性OTConnectionId及TOConnectionId修改为公开状态。</item>
	///            <item>WebSocketClient: 新增属性GetCarryHostAndPort用来标记HTTP请求头GET是否协议HOST信息，默认为false，DEMO界面优化，添加了输出示例代码，修复某些特殊服务器连接失败的bugs。</item>
	///            <item>OperateResult: 创建失败的类型返回方法新增一个重载的字符串参数，可以额外添加信息说明，方便后续查问题。</item>
	///            <item>SecsHsmsServer: 新增一个PublishSecsMessage重载方法，支持指定是否要求客户端返回，修复demo上勾选要求返回失败的bug。方法SendByCommand新增带wbit是否要求返回信息的重载方法。</item>
	///            <item>OpenProtocolNet: 修复创建报文参数异常，修复并优化demo界面问题，改从TcpNetCommunication继承，新增OpenProtocolServer配合进行本地测试。</item>
	///            <item>BeckhoffAdsNet: 修复读取的字节数大于10000时返回数据长度不正常的bug，修复读取bool数组时返回数据数量不正确的bug，虚拟服务器修复位读取时的返回数据不正确的bug。</item>
	///            <item>PanasonicMewtocol: 松下的Mewtocol协议支持读取 D，LD寄存器的位，地址格式为 D100.0， LD100.0 ， 影响范围包括网口及串口。</item>
	///            <item>BeckhoffAdsServer: 修复服务器端自己读写数组的某个索引的数据的时候，提示标签不存在的bug，暂不支持客户端读读写某个索引的标签数组。</item>
	///            <item>ModbusMappingAddress: 新增英威腾PLC的modbus地址转换实现，现在modbus可以注册这个地址转换，直接支持PLC原生地址。</item>
	///            <item>Demo: 数据表控件支持了曲线实现功能，kuka机器人界面增加更多的地址说明，fanuc的机床添加下载程序到本地。</item>
	///            <item>Demo: demo程序的PLC侧的数据表监视界面支持对值进行脚本运算，例如缩小100倍，输入 x/100.0  或是 x*0.01，也可以更加复杂三角函数运算</item>
	///            <item>Demo: demo程序优化，写入bool支持输入0, 1，然后demo关闭服务器自动停掉所有的定时器及线程操作，否则将会触发异常退出。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2024-9-3" version="12.1.0" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>FanucRobotServer: 修复客户端读取M,I,Q数据区的时候，服务器从内存数据区取数据时偏移量不正确的bug。</item>
	///            <item>SecsHsmsServer: 新增属性DeviceId，当调用方法 PublishSecsMessage 时使用该属性指定的设备ID信息来主动发送客户端。</item>
	///            <item>OmronFinsServer: 虚拟PLC支持了读取plc型号及plc状态的功能码，修复如果遇到不支持的功能码时候直接奔溃的bug，现在返回错误的命令。</item>
	///            <item>FanucSeries0i: 新增加接口读取刀组寿命ReadToolInfoByGroup，读取正在使用的刀组号ReadUseToolGroupId，以及清除刀组ClearToolGroup。</item>
	///            <item>HttpServer: 注册API接口时从之前的只支持GET/POST的方式，现在可以随意的支持其他的文本了，例如PUT</item>
	///            <item>DeviceServer: 虚拟服务器基类针对ReadFromCoreServer进行异常捕获，避免一些特殊情况下乱七八糟的数据导致虚拟服务异常奔溃。</item>
	///            <item>FormLogNetView: 新增属性OpenDialogDefaultPath，设置一个路径后，日志窗体点击打开文件时，将会默认打开该路径。</item>
	///            <item>SoftAuthorize: 授权相关类的实例化方法新增参数useHDD用来指示是否使用HDD参数信息，设置false可以避免插拔U盘导致机器码变化。</item>
	///            <item>Dtu: 重新设计的DTU功能，新增DTU管道，支持TCP,UDP,串口的设备直接变为DTU模式的设备，并直接支持从云上访问，原先的TCP管道删除AlienSession属性。</item>
	///            <item>DEMO界面的管道新增DTU管道选择，可以直接测试，TCP,串口调试界面支持直接转转远程DTU，文档地址：http://www.hsltechnology.cn/Doc/HslCommunication?chapter=HslCommChapter4-18</item>
	///            <item>CommunicationServer: 新增属性SessionsMax，用来设置服务器当前支持的最大会话数量，默认为uint.MaxValue</item>
	///            <item>Inovance: 汇川的AM系列的modbus地址，支持 MB100 地址，内部将自动转为字地址，所以必须为偶数开头。</item>
	///            <item>HttpServer: 新增方法UseHttps( )用来启动https，具体文档参考：http://www.hsltechnology.cn/Doc/HslCommunication?chapter=HslCommChapter6-5</item>
	///            <item>SiemensS7: S7虚拟服务器模拟真实PLC，读取结果数据大于226字节时返回错误码，优化SiemensS7Net的离散读取方法，现在地址传入任意个数或是任意长度，就能正确的分割读取，然后结果合并。</item>
	///            <item>DeviceSerialPort: 修复串口设备基类里，当管道设置了非PipeSerialPort串口管道时，调用打开方法Open()时，仍然打开原串口的bug。</item>
	///            <item>PipeMoxa: 新增一个基于MOXA公司开发的串口库的一个串口管道，在某些情况下比微软官方自带的串口好用，具体参考官网文档。</item>
	///            <item>MqttHelper: 直接捕获ParseMqttClientApplicationMessage解析的异常，防止一些乱七八糟情况下报文不对导致直接程序奔溃的bug。</item>
	///            <item>DEMO: 西门子的测试界面的时间，字符串等读写按钮增加光标悬停提示，提示调用的方法名称，方便大家使用。</item>
	///            <item>Demo: 修复创建示例代码功能中，如果碰到PipeTcpNet管道时，如果属性UseServerActivePush为true时，示例代码没有生成该文本的bug。</item>
	///            <item>Demo: 修复TCP、UDP调试界面下，当使用了定时发送之后，远程连接强制断开后，一直提示会话为空的bug。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2024-9-24" version="12.1.1" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>DeviceSerialPort: 修复当设置了非PipeSerialPort串口管道的时候，调用 IsOpen( ) 结果返回不正确的bug。</item>
	///            <item>DeviceServer: 新增方法 ServerStart( int tcpPort, int udpPort ), 用来同时启动TCP服务和UDP服务，三菱的DEMO测试增加启动功能测试。</item>
	///            <item>CommunicationServer: 修复服务器的类再只启动UDP服务器的情况下，服务器侧进行关闭操作时，引发后台线程异常的bug。</item>
	///            <item>MqttServer: 当客户端连接上来的时候，指定了keepAlive后，客户端的ActiveTimeSpan调整为1.5倍的keepAlive值，防止其他客户端极其特殊情况下可能误判下线。</item>
	///            <item>DLT645: DLT645-2007的串口网口类新增跳闸方法Trip( DateTime validTime )及合闸允许方法SwitchingOn( DateTime validTime )</item>
	///            <item>LSCnet: 进行了优化，全部使用连续读取的功能实现，添加携带站号的接口IReadWriteDeviceStation， LSCnet实现了该接口。</item>
	///            <item>SecsValue: 新增加ToSourceCode( )方法，获取获取该对象的源代码表示方式，用于DEMO程序自动生成发数据的源代码。</item>
	///            <item>Demo: 在PLC测试界面上的管道的功能当选择了DTU管道时，设备连接上来的时候，修复ID匹配不正确的bug。</item>
	///            <item>Demo: PLC测试界面上的读写按钮操作，都会在下面的示例代码里面显示出实际的代码情况，一些特殊功能测试的界面全部添加点击按钮生成示例代码，包括机床，机器人界面。</item>
	///            <item>SecsGem: Secs的服务器测试界面和客户端测试界面，功能码列表均支持了编辑和添加删除，然后点击保存连接的时候，自动将全部的功能码列表进行了存储，方便后续测试。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	///     <revision date="2024-10-30" version="12.1.2" author="Richard.Hu">
	///         <list type="bullet">
	///            <item>FujiSPBServer: DataFormat属性值修改为和客户端一致，DEMO界面上可以调整该值，修复英文界面下显示文本不正确的问题。</item>
	///            <item>XinJEServer: 支持输入 D，SD, HD 格式的地址，针对客户端使用XinJE专用协议的时候有效，如果用modbus协议的客户端，则还是输入modbus的地址</item>
	///            <item>SiemensS7Helper: 修复写入字符串的方法里，判断字节长度超出PLC最大设置定值时，字符串长度获取不正确的bug。</item>
	///            <item>MqttClient: 新增加一个属性 Tag, 可以用来携带任意的自定义对象，方便客户端做一些个性化的处理。</item>
	///            <item>Melsec: 三菱MC协议增加错误码17165的错误描述及解决办法，解决办法是将用户认证功能设置为禁用。</item>
	///            <item>LogNet: 日志类方法HslMessageFormat修改为虚方法，重写这个方法即可以实现存储时自定义的格式，DEMO界面在英文模式下显示英文，显示按钮的代码。</item>
	///            <item>MQTTServer: 新增方法UnRegisterMqttRpcApi用于卸载当前已经注册的指定接口，方便用于动态控制接口，此处的卸载是批量的。</item>
	///            <item>HttpServer: 新增方法UnRegisterHttpRpcApi用于卸载当前已经注册的某些接口，重复注册的时候将会覆盖旧的接口内容。</item>
	///            <item>SimpleHybirdLock: 新增进入锁的时间，当获取锁对象字符串的时候，输出当前锁的状态以及占用锁的时间。</item>
	///            <item>MqttServer: 修复ClientVerification事件签名里密码单词拼写不正确的bug。</item>
	///            <item>OmronFinsServer: 欧姆龙的FINS虚拟服务器支持了客户端使用 E9 这种地址的数据读写操作，不过所有的EM地址，E地址都使用同一个数据块。</item>
	///            <item>SecsServer: Secs服务器日志记录改为直接记录原始二进制的报文，修复服务器创建中文字符串格式的SecsValue时，长度显示不正确的bug。</item>
	///            <item>DLT645Server: 修复当StationMatch属性为true时，无论站号是否匹配都返回的bug，现在只有站号一直才支持读写数据操作。</item>
	///            <item>HslDeviceAddressAttribute: 新增字符串编码的属性，当类型是字符串有效，支持字符串数组，修复长度为0时应用于西门子读取字符串时读取字符串不正确的bug。</item>
	///            <item>Inovance: 汇川的PLC在AM系列下，读取MB1005单数地址的字符串时，也支持进行读取操作。</item>
	///            <item>IModbus: 所有Modbus设备新增属性BroadcastStation，设置0-255后，使用该站号时将不检测返回的报文，直接返回成功，ModbusServer适配站号0不返回数据。</item>
	///            <item>OmronFinsUdp: 属性SID信息不再进行自增操作，将由用户手动设置，然后demo界面支持手动设置操作。</item>
	///            <item>Demo: 批量读取的界面，新增是否字反转操作，方便部分的PLC可以看到一些特殊的数据情况。</item>
	///            <item>Demo: Secs服务器英文界面优化，西门子Smart200测试界面增加随机写入测试，并直接输出示例代码。</item>
	///            <item>Demo: MQTT测试界面增加正则过滤功能。设备类的测试界面添加数据模拟的功能，可以写入一些脚本实现的规律数据。</item>
	///            <item>Demo: HttpClient测试界面的ContentType修改为可输入。Secs界面修复英文环境下，标签名称不正确的问题</item>
	///            <item>Demo: 设备测试界面的读取功能，支持勾选屏蔽重复的值，只有数据变化的时候，才在读取界面里显示，重复就显示重复次数，菜单调试下新增设置显示毫秒时间的功能。</item>
	///            <item>新官网：http://www.hsltechnology.cn/，还有全新的使用文档的地址：http://www.hsltechnology.cn/Doc/HslCommunication</item>
	///            <item>本软件已经申请软件著作权，软著登记号：2020SR0340826，任何盗用软件，破解软件，未经正式合同授权而商业使用均视为侵权。</item>
	///         </list>
	///     </revision>
	/// </revisionHistory>
	[CompilerGenerated]
	public class NamespaceDoc
	{
	}
}
