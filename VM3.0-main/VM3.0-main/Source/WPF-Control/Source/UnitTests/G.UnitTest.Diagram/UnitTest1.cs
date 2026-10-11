using G.Controls.Diagram.Datas;
using G.Controls.Diagram.Presenter.NodeDatas;
using G.Controls.Diagram.Presenters.OpenCV;
using G.Extensions.NewtonsoftJson;
using G.Extensions.NewtonsoftJson.Jsonable;
using G.Services.Serializable;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace G.UnitTest.Diagram;

[TestClass]
public class UnitTest1
{
    [TestMethod]
    public void TestMethod1()
    {
        //OpenCVDiagramData openCVDiagramData = new OpenCVDiagramData();
        //ObservableCollection<INodeData> nodeDatas = new ObservableCollection<INodeData>();
        //nodeDatas.Add(new TextNodeData() { Text = "123456" });

        //openCVDiagramData.NodeDatas.Add(new TextNodeData() { Text = "123456" });
        ////NewtonsoftJsonSerializerService serializerService = new NewtonsoftJsonSerializerService();
        ////string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "1.json");
        ////serializerService.Save(path, nodeDatas);
        //var txt = JsonConvert.SerializeObject(openCVDiagramData, CreateSerializerSettings());
        //System.Diagnostics.Debug.WriteLine(txt);
    }

    private JsonSerializerSettings CreateSerializerSettings()
    {
        var setting = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.All,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore,//Ĭ��ֵ������,�������DefaultValue�������Ĭ��ֵ����������֮��ͻ����DefaultValue����
            Formatting = Formatting.Indented,// ���Ӹ�ʽ��ѡ��
                                             // �����������ĸ�������
                                             //NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals��
                                             //ReferenceLoopHandling = ReferenceLoopHandling.Ignore //����ѭ������
            ContractResolver = new CustomContractResolver(), // ʹ���Զ����ContractResolver�����ֶ�
            Converters = {
                    new H.Extensions.NewtonsoftJson.TypeConverterJsonConverter(),
                    new H.Extensions.NewtonsoftJson.EnumConverter(),
                    new H.Extensions.NewtonsoftJson.DateTimeConverter() }//�ⲿ�����л��ǻ��߼���������FilterBox����

        };
        return setting;
    }
    [TestMethod]
    public void TestMethod2()
    {
        //OpenCVDiagramData openCVDiagramData = new OpenCVDiagramData();
        //ObservableCollection<INodeData> nodeDatas = new ObservableCollection<INodeData>();
        //nodeDatas.Add(new TextNodeData() { Text = "123456" });

        //openCVDiagramData.NodeDatas.Add(new TextNodeData() { Text = "123456" });
        ////NewtonsoftJsonSerializerService serializerService = new NewtonsoftJsonSerializerService();
        ////string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "1.json");
        ////serializerService.Save(path, nodeDatas);
        //var txt = JsonConvert.SerializeObject(openCVDiagramData, CreateSerializerSettings());
        //System.Diagnostics.Debug.WriteLine(txt);

        MyNodeDatas myNodeDatas = new MyNodeDatas();
        myNodeDatas.NodeDatas.Add(new TextNodeData() { Text = "123456" });
        myNodeDatas.NodeDatas.Add(new ImageNodeData() { Text = "123456" });



        var setting = new JsonSerializerSettings
        {
            //TypeNameHandling = TypeNameHandling.All,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore,//Ĭ��ֵ������,�������DefaultValue�������Ĭ��ֵ����������֮��ͻ����DefaultValue����
            Formatting = Formatting.Indented,// ���Ӹ�ʽ��ѡ��
                                             // �����������ĸ�������
                                             //NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals��
                                             //ReferenceLoopHandling = ReferenceLoopHandling.Ignore //����ѭ������
                                             //ContractResolver = new CustomContractResolver(), // ʹ���Զ����ContractResolver�����ֶ�
                                             //Converters = {
                                             //        new H.Extensions.NewtonsoftJson.TypeConverterJsonConverter(),
                                             //        new H.Extensions.NewtonsoftJson.EnumConverter(),
                                             //        new H.Extensions.NewtonsoftJson.DateTimeConverter() }//�ⲿ�����л��ǻ��߼���������FilterBox����

        };
        var txt = JsonConvert.SerializeObject(myNodeDatas, setting);
        System.Diagnostics.Debug.WriteLine(txt);
    }

    [TestMethod]
    public void TestMethod3()
    {
        MyJsonable myNodeDatas = new MyJsonable();
        myNodeDatas.Value = 1234556;
        var setting = new JsonSerializerSettings
        {
            //TypeNameHandling = TypeNameHandling.All,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore,//Ĭ��ֵ������,�������DefaultValue�������Ĭ��ֵ����������֮��ͻ����DefaultValue����
            Formatting = Formatting.Indented,// ���Ӹ�ʽ��ѡ��
                                             // �����������ĸ�������
                                             //NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals��
                                             //ReferenceLoopHandling = ReferenceLoopHandling.Ignore //����ѭ������
                                             //ContractResolver = new CustomContractResolver(), // ʹ���Զ����ContractResolver�����ֶ�
            Converters = {
                                                     new H.Extensions.NewtonsoftJson.JsonableJsonConverter()
            }//�ⲿ�����л��ǻ��߼���������FilterBox����

        };
        var txt = JsonConvert.SerializeObject(myNodeDatas, setting);
        System.Diagnostics.Debug.WriteLine(txt);
       var v=  JsonConvert.DeserializeObject<MyJsonable>(txt, setting);
    }

}

[JsonDerivedType(typeof(TextNodeData))]
public class MyNodeDatas
{
    public ObservableCollection<INodeData> NodeDatas { get; set; } = new ObservableCollection<INodeData>();
}