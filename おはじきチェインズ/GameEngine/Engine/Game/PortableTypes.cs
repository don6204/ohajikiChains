// Small managed value/data types used by the migrated rules. No Unity assemblies,
// Unity player, or alternate rendering API are loaded by this engine.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ohajiki.Portable;

public class Object
{
    public string name = "";
    public static T Instantiate<T>(T value) where T : Object => (T)value.MemberwiseClone();
    public static void Destroy(Object? value) { }
    public static void DestroyImmediate(Object? value) { }
}
public class ScriptableObject : Object
{
    public static T CreateInstance<T>() where T : ScriptableObject, new() => new();
}
public sealed class Sprite
{
    public string Path = "";
}
public static class Application { public static bool isPlaying = true; }
public static class Resources
{
    public static Ohajiki.MasterGameData? Master;
    public static T? Load<T>(string name) where T : class => name == "OhajikiMasterData" ? Master as T : null;
}
public static class JsonUtility
{
    public static readonly JsonSerializerOptions Options = new() { IncludeFields = true, IgnoreReadOnlyProperties = true, Converters = { new NumericBooleanConverter() } };
    public static string ToJson<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T FromJson<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)!;
    sealed class NumericBooleanConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.TokenType == JsonTokenType.Number ? reader.GetInt32() != 0 : reader.GetBoolean();
        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
    }
}
public struct Vector2(float x, float y)
{
    public float x = x, y = y;
    public static Vector2 zero => new(0,0);
    public float sqrMagnitude => x*x+y*y;
    public float magnitude => MathF.Sqrt(sqrMagnitude);
    public Vector2 normalized => magnitude > 1e-5f ? this/magnitude : zero;
    public static Vector2 operator +(Vector2 a,Vector2 b)=>new(a.x+b.x,a.y+b.y);
    public static Vector2 operator -(Vector2 a,Vector2 b)=>new(a.x-b.x,a.y-b.y);
    public static Vector2 operator -(Vector2 a)=>new(-a.x,-a.y);
    public static Vector2 operator *(Vector2 a,float b)=>new(a.x*b,a.y*b);
    public static Vector2 operator *(float a,Vector2 b)=>b*a;
    public static Vector2 operator /(Vector2 a,float b)=>new(a.x/b,a.y/b);
    public static float Dot(Vector2 a,Vector2 b)=>a.x*b.x+a.y*b.y;
    public static Vector2 Reflect(Vector2 v,Vector2 normal)=>v-2*Dot(v,normal)*normal;
    public static Vector2 ClampMagnitude(Vector2 v,float max)=>v.sqrMagnitude>max*max?v.normalized*max:v;
    public static Vector2 MoveTowards(Vector2 current,Vector2 target,float distance)
    {var delta=target-current;return delta.magnitude<=distance?target:current+delta.normalized*distance;}
}
public struct Vector4(float x,float y,float z,float w)
{
    public float x=x,y=y,z=z,w=w;
    public static Vector4 one=>new(1,1,1,1);
    public float this[int i] => i switch {0=>x,1=>y,2=>z,3=>w,_=>throw new IndexOutOfRangeException()};
}
public static class Mathf
{
    public const float PI=MathF.PI;
    public static int RoundToInt(float v)=>checked((int)MathF.Round(v,MidpointRounding.ToEven));
    public static float Floor(float v)=>MathF.Floor(v);
    public static float Abs(float v)=>MathF.Abs(v);
    public static float Sign(float v)=>v>=0?1:-1;
    public static float Sin(float v)=>MathF.Sin(v);
    public static float Cos(float v)=>MathF.Cos(v);
    public static float Min(float a,float b)=>MathF.Min(a,b);
    public static int Min(int a,int b)=>Math.Min(a,b);
    public static float Max(float a,float b)=>MathF.Max(a,b);
    public static int Max(int a,int b)=>Math.Max(a,b);
    public static float Clamp(float v,float min,float max)=>Math.Clamp(v,min,max);
    public static int Clamp(int v,int min,int max)=>Math.Clamp(v,min,max);
    public static float Clamp01(float v)=>Math.Clamp(v,0,1);
    public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
    public static bool Approximately(float a,float b)=>Abs(b-a)<Max(.000001f*Max(Abs(a),Abs(b)),float.Epsilon*8);
}
[AttributeUsage(AttributeTargets.Class)] public sealed class CreateAssetMenuAttribute:Attribute { public string menuName=""; }
[AttributeUsage(AttributeTargets.Field)] public sealed class HeaderAttribute(string value):Attribute { public string Value {get;}=value; }
[AttributeUsage(AttributeTargets.Field)] public sealed class TooltipAttribute(string value):Attribute { public string Value {get;}=value; }
[AttributeUsage(AttributeTargets.Field)] public sealed class MinAttribute(float value):Attribute { public readonly float min=value; }
[AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute(float minimum,float maximum):Attribute { public readonly float min=minimum,max=maximum; }
[AttributeUsage(AttributeTargets.Field)] public sealed class HideInInspector:Attribute { }
[AttributeUsage(AttributeTargets.Field)] public sealed class TextAreaAttribute(int minLines=3,int maxLines=3):Attribute { public int Minimum {get;}=minLines; public int Maximum {get;}=maxLines; }
public enum RuntimeInitializeLoadType {SubsystemRegistration}
[AttributeUsage(AttributeTargets.Method)] public sealed class RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type):Attribute { public RuntimeInitializeLoadType Type {get;}=type; }
