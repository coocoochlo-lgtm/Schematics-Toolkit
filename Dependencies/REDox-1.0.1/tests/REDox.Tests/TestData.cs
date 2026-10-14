#pragma warning disable CS0414

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using REDox.Serialization;

namespace REDox.Tests;

public class TestData
{
    private static IEnumerable<object> GetPrimitiveInstances()
    {
        yield return 0.1;

        yield return 0.1f;

        yield return decimal.MaxValue;

        yield return decimal.MinValue;

        yield return Int128.MaxValue;

        yield return Int128.MinValue;
    }

    public static IEnumerable<(object src, object dest)> GetTransitionInstances()
    {
//        yield return (new DateTime(1922, 6, 4, 10, 10, 11), new object());
//        yield return (1.234, new object());
//        yield return (1.000m, new object());
        yield return (new SrcTest(), new DestTest());
        yield return (new SrcTest(), new object());
    }

    public static IEnumerable<object> GetSerializeInstances()
    {
        yield return new ArraySegment<int>(new[] { 1, 2, 3 });

        yield return TimeZoneInfo.Local;

        yield return new Range(new Index(10), new Index(20));

        yield return new TimeOnly(10, 24, 30);

        yield return new DateOnly(10, 1, 1);

        yield return new Dictionary<TimeOnly, string> { { new TimeOnly(10, 24, 30), "OK" } };

        yield return new Dictionary<DateOnly, string> { { new DateOnly(10, 1, 1), "OK" } };

        yield return new Dictionary<Version, string> { { new Version(1, 2, 3, 4), "OK" } };

        {
            yield return (1, true);

            yield return new ValueTuple<int, int>(1, 2);

            yield return new Tuple<int, int>(1, 2);
        }

        yield return new IntPtr(123);

        yield return new UIntPtr(123);

        yield return new Version(1, 2, 3, 4);

        {
            var nest = new DeepNest();
            var root = nest;

            for (var i = 0; i < 30; i++)
            {
                var sub = new DeepNest();

                nest.Sub = new[] { sub };

                nest = sub;
            }

            yield return root;
        }

        {
            var arr = new object[4];
            arr[0] = 123;
            arr[1] = arr;
            arr[2] = arr;
            arr[3] = true;
            yield return arr;
        }

        {
            var inst = new SpecializedCollection2();
            inst.Init();
            yield return inst;
        }

        yield return new ObjectModelCollection2();

        yield return new HalfTest();

        yield return new Int128Test();

        yield return new ReflectionProps();

        {
            var inst = new SpecializedCollection2();
            inst.Init();
            yield return inst;
        }

        yield return new BitVector32(123);

        yield return new Hashtable { { 1, 1 }, { 2, true }, { "A", 5 } };

        yield return new DataContractClass3();

        yield return new object();

        yield return new RefTest();

        yield return new CollectionTest2();

        yield return new RefLoop();

        yield return new ReadOnlyClass();

        yield return new[] { 1, 2, 3 };

        yield return new List<string> { "A", "B", "あいうえお" };

        yield return DBNull.Value;

        yield return new List<int> { 1, 2, 3 }.AsReadOnly();

        yield return new KeyValuePair<string, int>("A", 123);

        yield return new Stack(new object[] { 1, "SA", true });

        yield return new Queue(new object[] { 1, "QA", true });

        yield return new BitArray(new[] { true, false, true });

        yield return new NativeTest();

        yield return new DataContractAttribute();

        yield return new InheritCollectionTest2();

        foreach (var inst in GetInstances())
        {
            yield return inst;
        }
    }


    public static IEnumerable<object> GetInstances()
    {
//        yield return new System.Range(new Index(10), new Index(20));

        yield return new ArraySegment<int>(new[] { 1, 2, 3, 4, 5 }, 1, 3);

        yield return new BigInteger(1234567890123344);

        foreach (var inst in GetPrimitiveInstances())
        {
            yield return inst;
        }

        yield return new SpanTest();

        yield return new Dictionary<TimeOnly, string> { { new TimeOnly(10, 24, 30), "OK" } };

        yield return new Dictionary<Version, string> { { new Version(1, 2, 3, 4), "OK" } };


        yield return new HalfTest();

        yield return new Exceptions();

        yield return new NullReferenceException();

        yield return new NewtonsoftJsonClass();

        yield return new NewtonsoftJsonUser();

        yield return new NewtonsoftJsonUser2();

        yield return new NewtonsoftJsonUser3();

        yield return new SerializableTests();

        yield return new Int128Test();

        yield return -0;

        yield return new ExceptionTest();

        yield return new NullReferenceException();

        yield return new ConditionalClass();

        yield return new ConditionalStruct();

        yield return new InheritCollectionTest();

        yield return new NumericsTest();

        yield return new DrawingTest();

//        yield return new RefLoop();

        yield return new ObjectModelCollection();

        yield return new StaticTest();

        yield return new StaticTest2();

        yield return new SubClass();

        yield return new TestBase();

        yield return new InheritTest();

        yield return new RefTest();

        yield return new List<RefListTest>();

        yield return new SimpleClass();

        yield return new SimpleStruct();

        yield return new Numbers();

        yield return new SerializableBaseClass();

        yield return new SerializableClass();

        yield return new SerializableClass2();

        yield return new SpecialNumbers();

        yield return new TimeTest();

        yield return new StringTest();

        yield return new DataContractClass();

        yield return new DataContractClass3();

        yield return new EnumNumber();

        yield return new DictionaryTest();

        yield return new InterfaceTest();

        yield return new ObjectMember();

        yield return new SerializableTest();

        yield return new SerializableTest2();

        yield return new CallbackStructTest();

        yield return new CallbackTest();

        yield return new CallbackTest2();

        yield return new KnownTest();

        {
            var inst = new SpecializedCollection();
            inst.Init();
            yield return inst;
        }

        yield return new Uri("test://test/test");

        yield return new Uri("/test/test", UriKind.RelativeOrAbsolute);

        yield return new Stack(new object[] { 1, "A", true });

        yield return new Queue(new object[] { 1, "A", true });

        yield return new List<int> { 1, 2, 3 }.AsReadOnly();

        yield return new KeyValuePair<string, int>("A", 123);


        {
            var inst = new SpecializedCollection2();
            inst.Init();
            yield return inst;
        }

        {
            var inst = new CollectionTest();
            inst.Init();

            yield return inst;
        }

        {
            yield return new SerializableTest3();

            yield return new TextJsonClass();

            yield return new DateTime();

            yield return new AbstractTest();

            yield return new ConstructTest();

            yield return new RecordTest("Undefined", 999, DateTime.Now);

            yield return new InterfaceTest2();

            yield return new CollectionTest2();

            yield return new DataContractClass2();

            yield return new ConcurrentCollection();
        }

        {
            yield return new NativeTest();

//                yield return new BitArray(new bool[] { true, false, true });
        }

        {
            yield return new ConstructTest2();

            yield return new { name = "", value = true };

            yield return new TypeMember();

            yield return new[,] { { 1, 2, 3 }, { 4, 5, 6 } };

            yield return new PolymorphicTest();

            //TODO:
//            yield return new AttributeCompatibilityStress();

            //TODO:
//            yield return new ExtensionDataCompatibilityStress();

            yield return new ConstructorCompatibilityStress("ctor", 3, new[] { 1, 1, 2, 3, 5 });

            yield return new DictionaryKeyCompatibilityStress();
        }
    }
}

public enum TestEnum
{
    A,
    B,
    C
}

[DataContract(
    Name = "PersoNN",
    Namespace = "http://www.cohowinery.com/employeesあいう")]
public class Person : IExtensibleDataObject
{
    [DataMember] public string? Name;

    // To implement the IExtensibleDataObject interface,
    // you must implement the ExtensionData property. The property
    // holds data from future versions of the class for backward
    // compatibility.

    public ExtensionDataObject? ExtensionData { get; set; }
}

public class SpanTest
{
    private byte[] _byteTbl = new byte[] { 8, 9, 10 };
    private int[] _intTbl = new[] { 9, 8, 7 };

    /*
    public ReadOnlySpan<byte> ByteSpan
    {
        get
        {
            return _byteTbl;
        }
        set
        {
            _byteTbl = value.ToArray();
        }
    }

    public ReadOnlySpan<int> IntSpan
    {
        get
        {
            return _intTbl;
        }
        set
        {
            _intTbl = value.ToArray();
        }
    }
    */


    public Memory<byte> ByteMemorySpan { get; set; } = new(new byte[] { 1, 2, 3, 4, 5 });

    public Memory<int> IntMemorySpan { get; set; } = new(new[] { 1, 2, 3, 4, 5 });

    public ReadOnlyMemory<byte> ByteReadOnlyMemorySpan { get; set; } = new(new byte[] { 1, 2, 3, 4, 5 });

    public ReadOnlyMemory<int> IntReadOnlyMemorySpan { get; set; } = new(new[] { 1, 2, 3, 4, 5 });
}

[Serializable]
[KnownType(typeof(SubClass))]
public class Sub
{
    public int V = 1;

    [NonSerialized] private int V2 = 2;

    public Sub()
    {
    }

    // The special constructor is used to deserialize values.
    public Sub(SerializationInfo info, StreamingContext context)
    {
        // Reset the property value using the GetValue method.
        V2 = (int)info.GetValue("props", typeof(int))!;
    }

    // Implement this method to serialize data. The method is called
    // on serialization.
    public void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        // Use the AddValue method to specify serialized values.
        info.AddValue("props", V2, typeof(int));
    }
}

public class DeepNest
{
    public int Param1;

    public int Param2;

    public DeepNest[] Sub = Array.Empty<DeepNest>();
}

public class SrcTest
{
    public int Param { get; } = 123;

    public int[] Arr { get; } = new[] { 1, 2, 3 };

    public bool flag { get; set; } = true;
}

public class DestTest
{
    public int Param
    {
        set => RParam = value;
    }

    public int RParam { get; private set; } = 777;

    public bool Flag { get; set; }

    public int[] Arr { get; } = new[] { 5, 6, 7, 8 };
}

[DataContract]
public class StaticTest
{
    [DataMember] public const int ConstParam = 55;

    [DataMember] public const int _FieldConst = 123;

    [DataMember] public static int _StaticParam2 = 555;

    [DataMember] public readonly int _ROFieldConst = 123;

    [DataMember] public static int StaticParam { get; set; } = 123;
}

public class DrawingTest
{
    public Rectangle Rect { get; set; } = new(7, 8, 9, 10);

    public Point Point { get; set; } = new(7, 8);

    public Size Size { get; set; } = new(11, 22);

    public RectangleF RectF { get; set; } = new(7.7f, 8.8f, 9.9f, 10.01f);

    public PointF PointF { get; set; } = new(7.7f, 8.8f);

    public SizeF SizeF { get; set; } = new(11.1f, 22.2f);

    public Color Color1 { get; set; } = Color.FromArgb(0x4f, 0x4f, 0x4f, 0x4f);

    public Color Color2 { get; set; } = Color.FromArgb(0xff, 0xf, 0xff, 0xff);

    public Color Color3 { get; set; } = Color.AliceBlue;

    public Color Color4 { get; set; } = Color.FromArgb(64, Color.Red);

    public Color Color5 { get; set; } = SystemColors.ButtonFace;
}

public class StaticTest2 : StaticTest
{
    public const int BConstParam = 55;

    public const int _BFieldConst = 123;

    public static int _BStaticParam2 = 555;

    public readonly int _ROBFieldConst = 123;
    public static int BStaticParam { get; set; } = 123;
}

[DataContract]
public class TestBase
{
    [DataMember] public const int _FieldConst = 123;

    [DataMember] [JsonPropertyName("XYZ")] public readonly int _Field2;

    [DataMember] public Dictionary<int, int> Dict = new()
    {
        { 1, 2 }, { 3, 4 }
    };

    [DataMember(Name = "Pub")] public int FieldPub;

    [DataMember] private int _Field1 = 1;

    private List<int> _List = new(new[] { 1, 2, 3 });

    [DataMember] public Sub MySub { get; set; } = new();

    [DataMember] public Person MyPerson { get; set; } = new();

    [DataMember] public int Param { get; set; }

    [DataMember] public string? member2 { get; set; }

    [DataMember] public string? member3 { get; set; }

    [DataMember] public string? member4 { get; set; }

    [DataMember(EmitDefaultValue = false)] public string? IDmK { get; set; }

    [DataMember(EmitDefaultValue = false)] public int? OKok { get; set; }

    [JsonPropertyName("ok")] public float B { get; set; }

    [JsonInclude] public bool PrivateFlag { get; private set; } = true;

    [DataMember] public TestEnum EN { get; set; } = TestEnum.B;

    [OnSerializing]
    internal void OnSerializingMethod(StreamingContext context)
    {
        member2 = "This value went into the data file during serialization.";
    }

    [OnSerialized]
    internal void OnSerializedMethod(StreamingContext context)
    {
        member2 = "This value was reset after serialization.";
    }

    [OnDeserializing]
    internal void OnDeserializingMethod(StreamingContext context)
    {
        member3 = "This value was set during deserialization";
    }

    [OnDeserialized]
    internal void OnDeserializedMethod(StreamingContext context)
    {
        member4 = "This value was set after deserialization.";
    }
}

[DataContract]
public class InheritTest : TestBase
{
    [DataMember(Name = "DEF")] public int _Field3;

    public bool Flag { get; }

    public byte A { get; private set; }

    [DataMember] internal byte AA { get; private set; }

    [DataMember] protected byte AAA { get; private set; }

    [DataMember(Name = "ABC")] public int Prop4 { get; set; }
}

public class RefLoop
{
    public RefLoop()
    {
        Loop = this;
    }

    public RefLoop Loop { get; set; }
}

public class SubClass
{
    public int Param { get; set; } = 567;

    public int[] Arr { get; set; } = new[] { 7, 8 };
}

public class InheritClass : SubClass
{
    public int Param2 { get; set; } = 789;
}

public class ExceptionTest
{
    public int Param { get; set; } = 123;

    public int ParamR
    {
        get => throw new Exception();
        set { }
    }

    public int ParamW
    {
        get => 456;
        set => throw new Exception();
    }
}

public abstract class AbstractBase
{
    public int Param { get; set; } = 123;
}

public class ATest1 : AbstractBase
{
    public int Param1 { get; set; } = 111;
}

public class ATest2 : AbstractBase
{
    public int Param2 { get; set; } = 222;
}

public class AbstractTest
{
    public AbstractBase A { get; set; } = new ATest1();

    public AbstractBase B { get; set; } = new ATest2();
}

public class ConstructTest
{
    public Type1 T1 { get; set; } = new(123, true);

    public Type2 T2 { get; set; } = new(1234, true);

    public Type3 T3 { get; set; } = new(12345, true, true);

    public class Type1
    {
        public Type1(int param, bool flag)
        {
        }

        public int param { get; }

        public bool flag { get; }
    }

    public class Type2
    {
        public Type2(int param, bool flag)
        {
        }

        public int Param { get; }

        public bool Flag { get; }
    }

    public class Type3
    {
        public Type3(int param, bool flaG, bool Flag2)
        {
        }

        public int paRam { get; }

        public bool flaG { get; }

        public bool fLag2 { get; }
    }
}

public class ConstructTest2
{
    public Type3 T3 { get; set; } = new(12345, true, true);

    public Type4 T4 { get; set; } = new(true);

    public class Type3
    {
        public Type3(int param, bool flag, bool flag2)
        {
        }

        public int param { get; }

        public bool flag { get; }
    }

    public class Type4
    {
        public Type4(bool flag2)
        {
        }
    }
}

public class Exceptions
{
    public Exception? IndexOutExp
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        get
        {
            try
            {
                Func();

                return null;
            }
            catch (Exception e)
            {
                return e;
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Func()
    {
        var tbl = new int[1];

        return tbl[4];
    }
}

public class ReflectionProps
{
    public MethodBase? BaseMethod { get; set; }

    public PropertyInfo? Prop { get; set; }

    public FieldInfo? Field { get; set; }

    public MethodInfo? Method { get; set; }

    public MemberInfo? Member { get; set; }
}

[KnownType(typeof(SubClass))]
[KnownType(typeof(InheritClass))]
public class SimpleClass
{
    [DataMember(Name = "p_a_r_a_m")] private int _Param = 666;

    [DataMember] public int _Param2 = 777;

    public int Param { get; set; } = 123;

    public int PRParam { get; private set; } = 123;

    public int PROParam { get; protected set; } = 123;

    public int PIParam { get; internal set; } = 123;

    public int InitParam { get; init; } = 123;

    [DataMember(Name = "dpm")] public decimal DParam { get; set; } = 12m;

    public bool? NaFlag1 { get; set; } = null;

    [DataMember(Order = -10)] public bool? NaFlag2 { get; set; } = true;

    public int ROParam { get; } = 567;

    [DataMember(Order = 10)] public bool? ROFlag { get; } = true;

    public string[] ROArray { get; } = new[] { "A", "B" };

    public List<string> ROList { get; } = new() { "A", "B", "あいう" };

    public object OV1 { get; set; } = "ABC";

    [DataMember(Name = "pro")] private double Prop { get; set; } = 789;

    public object OV2 { get; set; } = 1.23;

    public object OV3 { get; set; } = new SubClass();

    public SubClass SV { get; set; } = new();

    public SubClass SV2 { get; set; } = new InheritClass();

    public InheritClass IV { get; set; } = new();

    [IgnoreDataMember] public int Ignore { get; set; }

    [DefaultValue(123)] public int DVal1 { get; set; } = 123;

    [DefaultValue(123)] public int DVal2 { get; set; } = 123;

    public Uri AUri { get; set; } = new("assets://test/test.ok");

    public Uri RUri { get; set; } = new("/test/test.ok", UriKind.Relative);

    public string[] EmptyTbl { get; set; } = new string[32];
}

[Serializable]
public class SerializableBaseClass
{
    protected int _BBParam3 = 456;

    public int _BParam = 456;

    private int _BParam2 = 456;

    private int _BParam3 = 456;
    public int BParam { get; set; } = 123;

    public int BZ { get; set; } = 1234;

    public int BA { get; set; } = 1235;
}

[Serializable]
public class SerializableClass : SerializableBaseClass
{
    public int _Param = 456;

    private int _Param2 = 456;
    internal int _ParamI = 456;

    protected int _ParamP = 456;

    public int Param { get; set; } = 123;

    public int Z { get; set; } = 1234;

    public int A { get; set; } = 1235;
}

[DataContract]
public class SerializableClass2 : SerializableBaseClass
{
    [DataMember] public int _Param = 456;

    [DataMember] private int _Param2 = 456;

    [DataMember] public int Param { get; set; } = 123;

    [DataMember] public int Z { get; set; } = 1234;

    [DataMember] public int A { get; set; } = 1235;
}

public struct CallbackStructTest
{
    [OnDeserialized]
    private void OnDeserialized(StreamingContext context)
    {
        DeserializeCounter--;
    }

    [OnDeserializing]
    private void OnDeserializing(StreamingContext context)
    {
        DeserializeCounter++;
    }


    [OnSerialized]
    private void OnSerialized(StreamingContext context)
    {
        SerializeCounter--;
    }

    [OnSerializing]
    private void OnSerializing(StreamingContext context)
    {
        SerializeCounter++;
    }

    public int DeserializeCounter { get; set; }

    public int SerializeCounter { get; set; }
}

public class CallbackTest
{
    public int DeserializeCounter { get; set; } = -1;

    public int SerializeCounter { get; set; } = -1;

    [DefaultValue(1)] public int Test { get; set; } = 1;

    [OnDeserialized]
    private void OnDeserialized(StreamingContext context)
    {
        DeserializeCounter--;
    }

    [OnDeserializing]
    private void OnDeserializing(StreamingContext context)
    {
        DeserializeCounter++;
    }


    [OnSerialized]
    private void OnSerialized(StreamingContext context)
    {
        SerializeCounter--;
    }

    [OnSerializing]
    private void OnSerializing(StreamingContext context)
    {
        SerializeCounter++;
    }
}

public class CallbackTest2 : CallbackTest
{
    [OnDeserialized]
    private void OnDeserialized(StreamingContext context)
    {
        DeserializeCounter--;
    }

    [OnDeserializing]
    private void OnDeserializing(StreamingContext context)
    {
        DeserializeCounter++;
    }


    [OnSerialized]
    private void OnSerialized(StreamingContext context)
    {
        SerializeCounter--;
    }

    [OnSerializing]
    private void OnSerializing(StreamingContext context)
    {
        SerializeCounter++;
    }
}

[Serializable]
public class SerializableTest : ISerializable
{
    public SerializableTest()
    {
    }

    public SerializableTest(SerializationInfo info, StreamingContext context)
    {
        var result = info.GetValue("name", typeof(bool));

        if (result != null)
        {
            Flag = (bool)result;
        }
    }

    public bool Flag { get; set; }

    public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        info.AddValue("name", Flag);
    }
}

[Serializable]
public class SerializableTest2 : SerializableTest
{
    public SerializableTest2()
    {
    }

    public SerializableTest2(SerializationInfo info, StreamingContext context) : base(info, context)
    {
        var result = info.GetValue("name2", typeof(bool));

        if (result != null)
        {
            Flag2 = (bool)result;
        }
    }

    public bool Flag2 { get; set; }

    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);

        info.AddValue("name2", Flag2);
    }
}

public class SerializableTest3 : SerializableTest
{
    public SerializableTest3()
    {
    }

    public SerializableTest3(SerializationInfo info, StreamingContext context)
    {
        var result = info.GetValue("name3", typeof(bool));

        if (result != null)
        {
            Flag3 = (bool)result;
        }
    }

    public bool Flag3 { get; set; }

    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        info.AddValue("name3", Flag3);
    }
}

[KnownType(typeof(SerializableTest2))]
public class SerializableTests
{
    public SerializableTest _Test1 { get; set; } = new();

    public SerializableTest _Test2 { get; set; } = new SerializableTest2();

//        public SerializableTest _Test3 { get; set; } = new SerializableTest3();
}

public interface ITest
{
    public bool FlagI { get; set; }
}

public class DeriTest : ITest
{
    public bool Flag { get; set; }
    public bool FlagI { get; set; }
}

public class RefTest
{
    private readonly SubClass _Inst = new();

    public RefTest()
    {
        A = _Inst;
        B = _Inst;
        C = _Inst;
    }

    public SubClass A { get; set; }

    public SubClass B { get; set; }

    public SubClass C { get; set; }
}

public class RefListTest
{
    public List<RefListTest> List { get; set; } = new();
}

public class ObjectModelCollection2
{
    public ObjectModelCollection2()
    {
        RCol2 = new ReadOnlyObservableCollection<int>(
            new ObservableCollection<int>(new[] { 1, 2, 3 }));
    }

    public ReadOnlyCollection<int> RCol { get; set; } = new(new[] { 1, 2, 3 });

    public ReadOnlyDictionary<string, int> RDict { get; set; } =
        new(new Dictionary<string, int> { { "A", 1 }, { "B", 2 } });

    public ReadOnlyObservableCollection<int> RCol2 { get; set; }
}

public class ObjectModelCollection
{
    public Collection<int> Col { get; set; } = new()
    {
        1, 2, 3
    };

    public ObservableCollection<int> Col2 { get; set; } = new()
    {
        1, 2, 3
    };
}

public class ConcurrentCollection
{
    public ConcurrentCollection()
    {
        Dict.TryAdd("Test", 123);
        Dict.TryAdd("AC", 5665);

        Queue.Enqueue("A");
        Queue.Enqueue("B");
        Queue.Enqueue("C");

        Stack.Push("A");
        Stack.Push("B");
        Stack.Push("C");
    }

    public ConcurrentDictionary<string, int> Dict { get; set; } = new();

    public ConcurrentQueue<string> Queue { get; set; } = new();

    public ConcurrentStack<string> Stack { get; set; } = new();
}

public class SpecializedCollection2
{
    public StringCollection? SCol { get; set; }

    public void Init()
    {
        SCol = new StringCollection();
        var myArr = new[] { "RED", "orange", "yellow", "RED", "green", "blue", "RED", "indigo", "violet", "RED" };
        SCol.AddRange(myArr);

        /*
        SDict = new StringDictionary();
        SDict.Add("red", "rojo");
        SDict.Add("green", "verde");
        SDict.Add("blue", "azul");
        */
    }

//        public StringDictionary? SDict { get; set; }
}

public class SpecializedCollection
{
    public Hashtable? HTbl { get; set; }

    public SortedList? SList { get; set; }

    public ListDictionary? LDict { get; set; }

    public HybridDictionary HDict { get; set; } = new() { { 1, 2 }, { "A", true }, { "B", "C" } };


    public LinkedList<int> LList { get; set; } = new(new[] { 1, 2, 3 });

    public void Init()
    {
        HTbl = new Hashtable { { 1, 1 }, { 2, true }, { "A", 5 } };

        SList = new SortedList();
        SList.Add("Third", "!");
        SList.Add("Second", "World");
        SList.Add("First", "Hello");

        LDict = new ListDictionary();
        LDict.Add("Braeburn Apples", "1.49");
        LDict.Add("Fuji Apples", "1.29");
    }
}

[DataContract]
[KnownType(typeof(Sub))]
public class KnownTest
{
    [DataMember] public object A { get; set; } = new Sub();

    [DataMember] public object B { get; set; } = new SubClass();
}

public class InheritCollectionTest
{
    public InheritList List { get; set; } = new() { 1, 2, 3 };

    public InheritSet Set { get; set; } = new() { 1, 2, 3 };

    public InheritDict Dict { get; set; } = new() { { 1, 2 }, { 2, 3 }, { 4, 5 } };

    public IList<int> RList { get; } = new InheritList { 1, 2, 3 };

    public ISet<int> RSet { get; } = new InheritSet { 1, 2, 3 };

    public IDictionary<int, int> IDict { get; } = new InheritDict { { 1, 2 }, { 2, 3 }, { 4, 5 } };

    public IReadOnlyList<int> RRList { get; } = new InheritList { 1, 2, 3 };

    public IReadOnlySet<int> RRSet { get; } = new InheritSet { 1, 2, 3 };

    public IReadOnlyDictionary<int, int> RDict { get; } = new InheritDict { { 1, 2 }, { 2, 3 }, { 4, 5 } };

    public InheritLinkedList LinkedList { get; set; } = new();

    [OnDeserializing]
    internal void OnDeserializingMethod(StreamingContext context)
    {
        IDict.Clear();
    }

    public class InheritList : List<int>
    {
        public int Param { get; set; } = 123;
    }

    public class InheritSet : HashSet<int>
    {
        public int Param { get; set; } = 123;
    }

    public class InheritDict : Dictionary<int, int>
    {
        public int Param { get; set; } = 123;
    }

    public class InheritLinkedList : LinkedList<int>
    {
        public InheritLinkedList()
        {
            AddFirst(1);
            AddFirst(2);
            AddFirst(3);
        }

        public int Param { get; set; } = 123;
    }
}

public class InheritCollectionTest2
{
    public InheritList List { get; set; } = new(123) { 1, 2, 3 };

    public InheritSet Set { get; set; } = new(123) { 1, 2, 3 };

    public InheritDict Dict { get; set; } = new(123) { { 1, 2 }, { 2, 3 }, { 4, 5 } };

    public IReadOnlyList<int> RRList { get; } = new InheritList(123) { 1, 2, 3 };

    public IReadOnlySet<int> RRSet { get; } = new InheritSet(123) { 1, 2, 3 };

    public IReadOnlyDictionary<int, int> RDict { get; } = new InheritDict(123) { { 1, 2 }, { 2, 3 }, { 4, 5 } };

    public class InheritList : List<int>
    {
        public InheritList(int p)
        {
            Param = p;
        }

        public int Param { get; set; } = 123;
    }

    public class InheritSet : HashSet<int>
    {
        public InheritSet(int p)
        {
            Param = p;
        }

        public int Param { get; set; } = 123;
    }

    public class InheritDict : Dictionary<int, int>
    {
        public InheritDict(int p)
        {
            Param = p;
        }

        public int Param { get; set; } = 123;
    }
}

[KnownType(typeof(double[]))]
public class CollectionTest
{
    private double[]? _Arr4;

    public LinkedList<int> LList { get; set; } = new(new[] { 1, 2, 3 });

//        public LinkedListNode<int> LNode { get; set; } = new LinkedListNode<int>(999);

    public Dictionary<string, int> Dict { get; set; } = new() { { "A", 1 }, { "B", 2 } };

//        public Dictionary<object, object> ODict { get; set; } = new Dictionary<object, object>() { {123, "A" },{ "B", "C" },{ TestEnum.C,TestEnum.B} };

    public List<int> List { get; set; } = new() { 0, 1, 2 };

    public HashSet<int> Set { get; set; } = new() { 0, 1, 2 };

    public DictionaryEntry Entry { get; set; } = new("key", "value");

    public DictClass Dict2 { get; set; } = new() { { "A", 1 }, { "B", 2 } };

    public ListClass List2 { get; set; } = new() { 0, 1, 2 };

    public SetClass Set2 { get; set; } = new() { 0, 1, 2 };

    public SortedList<string, int> SList { get; set; } = new() { { "A", 1 }, { "B", 2 } };

    public SortedDictionary<string, int> SDict { get; set; } = new() { { "A", 1 }, { "B", 2 } };

    public SortedSet<int> SSet { get; set; } = new() { 1, 2, 3 };

    public ObservableCollection<int> ObsCol { get; set; } = new() { 1, 2, 3 };

    public ArrayList List3 { get; set; } = new() { 1, "A", true };

    public int[] Arr { get; } = new[] { 1, 2, 3 };

    public double[]? Arr4 { get; set; }

    public IList<double>? LTest
    {
        get => Arr4;
        set => Arr4 = value?.ToArray();
    }

    public ICollection<double>? Col
    {
        get => Arr4;
        set => Arr4 = value?.ToArray();
    }

    public IEnumerable<double>? Enm
    {
        get => _Arr4;
        set => _Arr4 = value?.ToArray();
    }

    public void Init()
    {
        Arr4 = new double[2];
        Arr4[0] = Random.Shared.NextDouble();
        Arr4[1] = Random.Shared.NextDouble();

        _Arr4 = new double[1];
        _Arr4[0] = Random.Shared.NextDouble();
    }

    public class DictClass : Dictionary<string, int>
    {
        public int Param { get; set; }
    }

    public class ListClass : List<int>
    {
        public int Param { get; set; }
    }

    public class SetClass : HashSet<int>
    {
        public int Param { get; set; }
    }
}

public class CollectionTest2
{
    public Stack<int> Stack { get; set; } = new(new[] { 1, 2, 3 });

    public Queue<int> Queue { get; set; } = new(new[] { 1, 2, 3 });

    public SortedList SList3 { get; set; } = new() { { "OK", 1 } };

    public IList<int> Arr2 { get; } = new[] { 1, 2, 3 };

    public ICollection Col { get; } = new[] { 1, 2, 3 };
}

public class NumericsTest
{
    public Complex Complex { get; set; } = new(1.1, 2.2);

    public Vector2 Vec2 { get; set; } = new(1, 2);

    public Vector3 Vec3 { get; set; } = new(1, 2, 3);

    public Vector4 Vec4 { get; set; } = new(1, 2, 3, 4);

    public Vector<float> Vec { get; set; } = new();

    public Matrix3x2 Mat32 { get; set; } = new(1, 2, 3, 4, 5, 6);

    public Matrix4x4 Mat44 { get; set; } = new(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16);

    public Plane Plane { get; set; } = new(1, 2, 3, 0);

    public Quaternion Quat { get; set; } = new(1, 2, 3, 4);
}

public class EnumeratorTest : IEnumerable<int>
{
    public int Param { get; set; } = 666;

    public IEnumerator<int> GetEnumerator()
    {
        yield return 123;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

public class ListTest : ICollection<int>
{
    public void Add(int v)
    {
    }

    public void Clear()
    {
    }

    public bool Contains(int v)
    {
        return false;
    }

    public void CopyTo(int[] arr, int index)
    {
    }

    public bool Remove(int v)
    {
        return false;
    }

    public int Count => 1;

    public bool IsReadOnly => false;

    public IEnumerator<int> GetEnumerator()
    {
        yield return 123;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

[KnownType(typeof(HashSet<int>))]
[KnownType(typeof(DeriTest))]
public class InterfaceTest2
{
    private readonly Dictionary<string, int> _RDict = new();

    private Dictionary<string, int> _Dict = new()
    {
        { "A", 123 }, { "B", 456 }
    };

    private List<int> _List = new() { 1, 2, 3 };
    private List<int> _List2 = new() { 1, 2, 3 };

    private HashSet<int> _Set = new() { 1, 2, 3 };

    public IDictionary<string, int> RDict => _RDict;

    public ISet<int> Set
    {
        get => _Set;
        set => _Set = new HashSet<int>(value);
    }

    public IReadOnlyDictionary<string, int> RODict
    {
        get => _Dict;
        set => _Dict = new Dictionary<string, int>(value);
    }

    public IReadOnlyList<int> ROList
    {
        get => _List2;
        set => _List = new List<int>(value);
    }

    public ICollection<int> Collection
    {
        get => _List2;
        set => _List2 = new List<int>(value);
    }

    public IReadOnlyCollection<int> ROCollection
    {
        get => _List2;
        set => _List2 = new List<int>(value);
    }

    public IEnumerable<int> REnumerable => _List2;

    public IEnumerable<int> Enumerable
    {
        get => _List2;
        set => _List = new List<int>(value);
    }

    public IEnumerable<int> RETest { get; } = new EnumeratorTest();

    public IEnumerable RETest2 { get; } = new EnumeratorTest();

    public IEnumerable<KeyValuePair<string, int>> DETest => _Dict;
}

[KnownType(typeof(HashSet<int>))]
[KnownType(typeof(DeriTest))]
public class InterfaceTest
{
    private readonly List<int> _List2 = new() { 1, 2, 3 };


    private readonly Dictionary<string, int> _RDict = new();

    private readonly HashSet<int> _Set = new() { 1, 2, 3 };

    private Dictionary<string, int> _Dict = new()
    {
        { "A", 123 }, { "B", 456 }
    };

    private List<int> _List = new() { 1, 2, 3 };

    public ICollection<int> ListTest { get; set; } = new ListTest();

    public ICollection<int> RListTest { get; } = new ListTest();

    public ICollection<int>? NCollection { get; set; } = null;

    public IList<int>? NList { get; set; } = null;

    public IDictionary<string, int>? NDict { get; set; } = null;

    public IReadOnlyCollection<int>? NROCollection { get; set; } = null;

    public IReadOnlyList<int>? NROList { get; set; } = null;

    public IReadOnlyDictionary<string, int>? NRODict { get; set; } = null;

    public ISet<int>? NSet { get; set; } = null;

    public IReadOnlySet<int>? NROSet { get; set; } = null;

    public IList<int> List
    {
        get => _List;
        set => _List = new List<int>(value);
    }

    public IList<int> RList => _List;

    public IDictionary<string, int> Dict
    {
        get => _Dict;
        set => _Dict = new Dictionary<string, int>(value);
    }

    public IDictionary<string, int> RDict => _RDict;

    public ISet<int> RSet => _Set;

    public IReadOnlySet<int> ROSet => _Set;

    public IReadOnlyDictionary<string, int> RODict => _Dict;

    public ITest Test { get; } = new DeriTest();

    public IReadOnlyList<int> ROList => _List2;

    public ICollection<int> Collection => _List2;

    public IReadOnlyCollection<int> ROCollection => _List2;

    public EnumeratorTest ETest { get; } = new();
}

public class NativeTest
{
    public nint IP { get; set; } = new(123);

    public nint IPMin { get; set; } = nint.MinValue;

    public nint IPMax { get; set; } = nint.MaxValue;

    public nuint UIP { get; set; } = new(123);

    public nuint UIPMin { get; set; } = nuint.MinValue;

    public nuint UIPMax { get; set; } = nuint.MaxValue;
}

public class EnumNumber
{
    [Flags]
    public enum ByteFlag : sbyte
    {
        None,
        A = 1,
        B = 4,
        C = 16,
        D = 32
    }

    public enum ByteKind : sbyte
    {
        Default = -1,
        A = 3,
        B,
        C,
        D = 127
    }

    [Flags]
    public enum Flag
    {
        None,
        A = 1,
        B = 4,
        C = 16,
        D = 32
    }

    public enum Kind
    {
        Default,
        A,
        B,
        C,
        D = 1000
    }

    public Kind KindValue { get; set; } = Kind.B;

    public ByteKind ByteKindValue { get; set; } = ByteKind.B;

    public Flag FlagValue { get; set; } = Flag.B | Flag.D;

    public ByteFlag ByteFlagValue { get; set; } = ByteFlag.B | ByteFlag.D;
}

public class DictionaryTest
{
    public Dictionary<Int128, Int128> DictInt128 { get; set; } = new()
    {
        { Int128.MaxValue, Int128.MinValue }
    };

    public Dictionary<UInt128, UInt128> DictUInt128 { get; set; } = new()
    {
        { UInt128.MaxValue, UInt128.MinValue }
    };

    public Dictionary<Half, Half> DictHalf { get; set; } = new()
    {
        { Half.MaxValue, Half.MinValue }
    };

    public Dictionary<TimeOnly, TimeOnly> DictTimeOnly { get; set; } = new()
    {
        { new TimeOnly(1, 1), new TimeOnly(2, 2) }
    };

    public Dictionary<DateOnly, DateOnly> DictDateOnly { get; set; } = new()
    {
        { new DateOnly(1999, 1, 1), new DateOnly(2019, 2, 4) }
    };

    public Dictionary<string, int> Dict { get; set; } = new()
    {
        { "ABC", 10 },
        { "DEF", 123 },
        { "HIJ", -10 }
    };

    public Dictionary<float, int> Dict2 { get; set; } = new()
    {
        { 0.1f, 10 },
        { 0.4f, 123 },
        { 0.5f, -10 }
    };

    public Dictionary<EnumNumber.ByteKind, EnumNumber.Flag> Dict3 { get; set; } = new()
    {
        { EnumNumber.ByteKind.B, EnumNumber.Flag.A },
        { EnumNumber.ByteKind.C, EnumNumber.Flag.A | EnumNumber.Flag.D },
        { EnumNumber.ByteKind.D, EnumNumber.Flag.None }
    };

    public HashSet<int> HSet { get; set; } = new() { 1, 2, 3 };
}

public class ReadOnlyClass
{
    public SubClass Sub { get; } = new();

    public Dictionary<string, int> Dict { get; } = new() { { "A", 123 } };
}

[JsonPolymorphic]
[JsonDerivedType(typeof(SuccesfulResult), "success")]
[JsonDerivedType(typeof(ErrorResult), "failed")]
public abstract record Result;

public record SuccesfulResult : Result;

public record ErrorResult(string Error) : Result;

public class PolymorphicTest
{
    public Result Result1 = new SuccesfulResult();

    public Result Result2 = new ErrorResult("bad");
}

public sealed class AttributeCompatibilityStress
{
    [JsonProperty("legacy_id", Order = -10, Required = Required.Always)]
    [JsonPropertyName("legacy_id")]
    [DataMember(Name = "legacy_id", Order = -10, IsRequired = true)]
    public string Id { get; set; } = "ID-001";

    [JsonProperty(Order = -5, NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    [JsonPropertyName("URLValue")]
    [JsonPropertyOrder(-5)]
    public string URLValue { get; set; } = "https://example.test/a?b=1&c=あ";

    [DefaultValue(0)]
    [JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [DataMember(EmitDefaultValue = false)]
    public int DefaultSuppressed { get; set; }

    [JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NullSuppressed { get; set; }

    [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
    public List<TestEnum> EnumValues { get; set; } = new() { TestEnum.A, TestEnum.B, TestEnum.C };

    public Dictionary<string, object?> MixedObjectValues { get; set; } = new()
    {
        ["int"] = 1,
        ["long"] = long.MaxValue,
        ["decimal"] = 1234567890.123456789m,
        ["dateTimeOffset"] = new DateTimeOffset(2024, 2, 29, 12, 34, 56, TimeSpan.FromHours(9)),
        ["uri"] = new Uri("https://example.test/path?q=1"),
        ["enum"] = TestEnum.B,
        ["nested"] = new Dictionary<string, object?>
        {
            ["null"] = null,
            ["array"] = new object?[] { 1, "two", false }
        }
    };
}

public sealed class ExtensionDataCompatibilityStress
{
    public int Known { get; set; } = 7;

    [Newtonsoft.Json.JsonExtensionData]
    public IDictionary<string, object?> NewtonsoftExtensionData { get; set; } = new Dictionary<string, object?>
    {
        ["unknown_number"] = 100,
        ["unknown_text"] = "newtonsoft",
        ["unknown_object"] = new Dictionary<string, object?>
        {
            ["flag"] = true,
            ["values"] = new[] { 3, 1, 4 }
        }
    };

    [System.Text.Json.Serialization.JsonExtensionData]
    public IDictionary<string, object?> SystemTextJsonExtensionData { get; set; } = new Dictionary<string, object?>
    {
        ["extraNumber"] = 200,
        ["extraText"] = "system-text-json",
        ["extraArray"] = new object?[] { "x", 9, null }
    };
}

public sealed class ConstructorCompatibilityStress
{
    [Newtonsoft.Json.JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    public ConstructorCompatibilityStress(string name, int count, IEnumerable<int>? values)
    {
        Name = name;
        Count = count;
        Values = values?.ToArray() ?? Array.Empty<int>();
    }

    public string Name { get; }

    public int Count { get; }

    public IReadOnlyList<int> Values { get; }

    [JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Reuse)]
    public List<string> MutableValues { get; } = new() { "existing", "value" };

    public string PrivateSetter { get; private set; } = "private-setter";
}

public sealed class DictionaryKeyCompatibilityStress
{
    public Dictionary<string, int> StringKeys { get; set; } = new()
    {
        ["URLValue"] = 1,
        ["urlValue"] = 2,
        ["url_value"] = 3,
        ["kebab-key"] = 4,
        ["✋"] = 5
    };

    public Dictionary<TestEnum, string> EnumKeys { get; set; } = new()
    {
        [TestEnum.A] = "alpha",
        [TestEnum.B] = "bravo"
    };

    public SortedDictionary<int, string> NumericKeys { get; set; } = new()
    {
        [-1] = "negative",
        [0] = "zero",
        [42] = "answer"
    };
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
[Serialization.DataObject(NamingPolicyType = typeof(CamelCaseNamingPolicy))]
public class NewtonsoftJsonUser
{
    [JsonProperty("Test")]
    [DataProperty("Test")]
    public string? FirstName { get; set; }

    [JsonProperty("Test2", NullValueHandling = Newtonsoft.Json.NullValueHandling.Include)]
    [DataProperty("Test2", NullValueHandling = Serialization.NullValueHandling.Include)]
    public string? LastName { get; set; }

    [JsonProperty("Test3", DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
    [DataProperty("Test3", DefaultValueHandling = Serialization.DefaultValueHandling.Ignore)]
    public int Param { get; set; }

    [JsonProperty(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
    [DataProperty(NamingPolicyType = typeof(SnakeCaseLowerNamingPolicy))]
    public int SnakeRating { get; set; } = 567;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
[Serialization.DataObject(NamingPolicyType = typeof(SnakeCaseLowerNamingPolicy))]
public class NewtonsoftJsonUser2 : NewtonsoftJsonUser
{
    public string FirstName2 { get; set; } = "fname";
    public string LastName2 { get; set; } = "lname";

    [JsonProperty(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    [DataProperty(NamingPolicyType = typeof(CamelCaseNamingPolicy))]
    public int SnakeRating2 { get; set; } = 789;

    [JsonProperty("SnakeRatingTest", NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    [DataProperty("SnakeRatingTest", NamingPolicyType = typeof(CamelCaseNamingPolicy))]
    public int SnakeRating3 { get; set; } = 789;
}

public class NewtonsoftJsonUser3 : NewtonsoftJsonUser
{
    public string FirstName3 { get; set; } = "fname";
    public string LastName3 { get; set; } = "lname";
    public int SnakeRating3 { get; set; } = 321;
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
[Serialization.DataObject(NamingPolicyType = typeof(SnakeCaseLowerNamingPolicy))]
public class NewtonsoftJsonClass
{
    [JsonProperty(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    [DataProperty(NamingPolicyType = typeof(CamelCaseNamingPolicy))]
    public bool FlagTest1 { get; set; }

    [JsonProperty(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
    [DataProperty(NamingPolicyType = typeof(SnakeCaseLowerNamingPolicy))]
    public bool FlagTest2 { get; set; }

    public byte[] Bin { get; set; } = new byte[] { 1, 2, 3, 4, 5, 6, 7 };

    public sbyte[] Bin2 { get; set; } = new sbyte[] { 1, 2, 3, 4, 5, 6, 7 };

    [JsonProperty("flg3", Required = Required.Always)]
    [DataProperty("flg3")]
    public bool FlagTest3 { get; set; } = true;

    [Newtonsoft.Json.JsonIgnore]
    [DataIgnore]
    public bool FlagTest4 { get; set; } = true;

    [Newtonsoft.Json.JsonConverter(typeof(UnixDateTimeConverter))]
    public DateTime DT { get; set; } = DateTime.Now;

//    [Newtonsoft.Json.JsonProperty(ItemConverterType = typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
//        [REDox.Serialization.DataProperty(ItemConverterType = typeof(REDox.Serialization.StringEnumConverter))]
    public List<TestEnum> MyList { get; set; } = new() { TestEnum.A, TestEnum.B, TestEnum.C };
}

public class TextJsonClass
{
    [JsonPropertyName("✋")]
    [JsonPropertyOrder(100)]
    public bool Flag { get; set; } = true;

    [JsonPropertyName("あいうえお")] public int Prop { get; set; } = 9;

    [JsonInclude] [JsonPropertyOrder(10)] public float ROProp { get; private set; } = 0.12f;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool Flag2 { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Flag3 { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Flag4 { get; set; } = true;

    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name1 { get; set; } = null;

    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Name2 { get; set; } = "OK";

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public float Fval { get; set; } = 0.234f;

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public List<int> MyList { get; set; } = new() { 1, 2, 3 };

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public Dictionary<int, int> MyDict { get; set; } = new() { { 1, 2 }, { 3, 4 }, { 5, 6 } };

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public HashSet<int> MySet { get; set; } = new() { 1, 2, 3 };

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public IReadOnlyCollection<int> MyCol { get; set; } = new[] { 1, 2, 3 };

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public IEnumerable<int> MyEnm { get; set; } = new[] { 1, 2, 3 };
}

public class ConditionalClass
{
    public int Param1 { get; set; } = 123;

    public int Param2 { get; set; } = 123;

    public bool ShouldSerializeParam1()
    {
        return false;
    }

    public bool ShouldSerializeParam2()
    {
        return true;
    }
}

public struct ConditionalStruct
{
    public int Param1 { get; set; }

    public int Param2 { get; set; }

    public bool ShouldSerializeParam1()
    {
        return false;
    }

    public bool ShouldSerializeParam2()
    {
        return true;
    }
}

[DataContract(Name = "test")]
public class DataContractClass
{
    [DataMember(Order = 200)] private int _Iparam = 123;

    private int _Iparam2 = 444;

    [DataMember] public int _Pub = 555;

    [DataMember(Name = "pam", Order = 100, EmitDefaultValue = true)]
    public bool Param { get; set; }

    [DataMember(Name = "✋", Order = 10, EmitDefaultValue = false)]
    public bool Param2 { get; set; } = true;

    [DataMember(Name = "かきくけこ", Order = 20, EmitDefaultValue = false)]
    public bool? Param3 { get; set; }

    [DataMember] [IgnoreDataMember] public int IgParam { get; set; }

    [DataMember(Order = 0)] private float FParam { get; set; } = 1.2f;
}

[DataContract(Name = "test", Namespace = "test", IsReference = true)]
public class DataContractClass2
{
    [DataMember] private int _Iparam = 123;
}

[Serializable]
public class DataContractClass3 : DataContractClass
{
    [DataMember(Order = 100)] private int _Iparam3 = 123;

    [DataMember(Name = "ok")] public int _Iparam4 = 123;

    [DataMember(Name = "ok2")] public int Param5 { get; set; } = 11;
}

[KnownType(typeof(SubClass))]
[KnownType(typeof(SimpleClass))]
[KnownType(typeof(SimpleStruct))]
[KnownType(typeof(EnumNumber.Flag))]
[KnownType(typeof(int[]))]
public class ObjectMember
{
    public object OVal { get; set; } = new();

    public object CVal { get; set; } = new SimpleClass();

    public object SVal { get; set; } = new SimpleStruct();

    public object EVal { get; set; } = EnumNumber.Flag.C | EnumNumber.Flag.D;

    public object IVal { get; set; } = 123;

    public object DVal { get; set; } = 0.1m;

    public object IArr { get; set; } = new[] { 1, 2, 3 };

    public object OArr { get; set; } = new[] { "ABC", new object(), new SubClass() };

    public object OArr2 { get; set; } = new object[] { new SimpleClass() };

    public object Bin { get; set; } = new byte[] { 1, 2, 3, 4, 5, 6, 7 };
}

public class TypeMember
{
    public Type TypeInfo1 { get; } = typeof(string);

    public Type? TypeInfo2 { get; set; } = null;

    public Type TypeInfo3 { get; set; } = typeof(string);
}

public record RecordTest(string name, int value, DateTime dt);

public struct SimpleStruct
{
    public int Param { get; set; }

    public bool? NaFlag1 { get; set; }

    public bool? NaFlag2 { get; set; }
}

public class StringTest
{
    public string? TextNull { get; set; } = null;

    public string TextEmpty { get; set; } = string.Empty;

    public string Text { get; set; } = "ABC";

    public string Text2 { get; set; } = "あいうえお";

    public string Text3 { get; set; } = "ÃÃÚÚÚÚÃÃ";

    public string Text4 { get; set; } = "/*~{} '&<> \\ \t\n\r\"\0";

    public string Text5 { get; set; } = " ( ˘ω˘ ✋👂)";

    public string[] TextArray { get; set; } = new[] { "A", "あいう", "👂", "*~{} '&<> \\" };

    public List<string> TextList { get; set; } = new() { "A", "あいう", "👂", "*~{} '&<> \\" };
}

public class TimeTest
{
    public DateTime DT { get; set; } = DateTime.Now;

    public DateTime DTU { get; set; } = DateTime.Now.ToUniversalTime();

    public DateTime DTL { get; set; } = DateTime.Now.ToLocalTime();

    public DateTime DTS { get; set; } = new(2019, 3, 4);

    public DateTimeOffset DTO { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset DTOU { get; set; } = DateTimeOffset.Now.ToUniversalTime();

    public DateTimeOffset DTOL { get; set; } = DateTimeOffset.Now.ToLocalTime();

    public DateTimeOffset DTOS { get; set; } = new(2019, 3, 4, 4, 5, 6, TimeSpan.FromHours(1));

    public TimeSpan TS1 { get; set; } = TimeSpan.FromMilliseconds(11);

    public TimeSpan TS2 { get; set; } = TimeSpan.FromMinutes(11);

    public TimeSpan T2S2 { get; set; } = TimeSpan.FromMinutes(22);

    public TimeSpan TS2_ { get; set; } = TimeSpan.FromMinutes(33);

    public TimeSpan TS_3 { get; set; } = TimeSpan.FromDays(11);

    public TimeSpan TSa3 { get; set; } = TimeSpan.FromDays(30);
}

public class Int128Test
{
    public Int128 IntMin { get; set; } = Int128.MinValue;

    public Int128 IntMax { get; set; } = Int128.MaxValue;

    public Int128 IntVal { get; set; } = new(123, 456);

    public UInt128 UIntMin { get; set; } = UInt128.MinValue;

    public UInt128 UIntMax { get; set; } = UInt128.MaxValue;

    public UInt128 UIntVal { get; set; } = new(123, 456);
}

public class HalfTest
{
    public Half PositiveInfinity { get; set; } = Half.PositiveInfinity;

    public Half NegativeInfinity { get; set; } = Half.NegativeInfinity;

    public Half NaN { get; set; } = Half.NaN;

    public Half MinHalf { get; set; } = Half.MinValue;

    public Half MaxHalf { get; set; } = Half.MinValue;

    public Half Value { get; set; } = (Half)0.123;
}

public class SpecialNumbers
{
    public float PositiveInfinity32 { get; set; } = float.PositiveInfinity;

    public float NegativeInfinity32 { get; set; } = float.NegativeInfinity;

    public float NaN32 { get; set; } = float.NaN;

    public double PositiveInfinity64 { get; set; } = double.PositiveInfinity;

    public double NegativeInfinity64 { get; set; } = double.NegativeInfinity;

    public double NaN64 { get; set; } = double.NaN;
}

public class Numbers
{
    public byte MinByte { get; set; } = byte.MinValue;

    public byte MaxByte { get; set; } = byte.MaxValue;

    public byte ByteValue { get; set; } = 123;

    public sbyte MinSByte { get; set; } = sbyte.MinValue;

    public sbyte MaxSByte { get; set; } = sbyte.MaxValue;

    public sbyte SByteValue { get; set; } = -123;

    public ushort MinUInt16 { get; set; } = ushort.MinValue;

    public ushort MaxUInt16 { get; set; } = ushort.MaxValue;

    public short MinInt16 { get; set; } = short.MinValue;

    public short MaxInt16 { get; set; } = short.MaxValue;

    public uint MinUInt32 { get; set; } = uint.MinValue;

    public uint MaxUInt32 { get; set; } = uint.MaxValue;

    public int MinInt32 { get; set; } = int.MinValue;

    public int MaxInt32 { get; set; } = int.MaxValue;

    public ulong MinUInt64 { get; set; } = ulong.MinValue;

    public ulong MaxUInt64 { get; set; } = ulong.MaxValue;

    public long MinInt64 { get; set; } = long.MinValue;

    public long MaxInt64 { get; set; } = long.MaxValue;

    public float MinSingle { get; set; } = float.MinValue;

    public float MaxSingle { get; set; } = float.MaxValue;

    public float SingleValue { get; set; } = 0.123f;

    public double MinDouble { get; set; } = double.MinValue;

    public double MaxDouble { get; set; } = double.MaxValue;

    public double DoubleValue { get; set; } = 0.00000000123;

    public decimal MinDecimal { get; set; } = decimal.MinValue;

    public decimal MaxDecimal { get; set; } = decimal.MaxValue;

    public decimal Decimal1 { get; set; } = 0.000001m;

    public decimal Decimal0 { get; set; } = 0.000000m;

    public decimal Decimal2 { get; set; } = 0.002000m;

    public double MinusZero { get; set; } = -0.0;
}