using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace Ohajiki.Engine;

sealed class DynamicProperty(string name, string label, string category, Type type, Func<object?> getter, Action<object?> setter, string description, string[]? choices = null) : PropertyDescriptor(name, [new DisplayNameAttribute(label), new CategoryAttribute(category), new DescriptionAttribute(description)])
{
    public override Type ComponentType => typeof(InspectorObject);
    public override bool IsReadOnly => false;
    public override Type PropertyType => type;
    public override bool CanResetValue(object component) => false;
    public override object? GetValue(object? component) => getter();
    public override void SetValue(object? component, object? value) => setter(value);
    public override void ResetValue(object component) { }
    public override bool ShouldSerializeValue(object component) => false;
    public override TypeConverter Converter => choices is { Length: > 0 } ? new ChoiceConverter(choices) : type == typeof(bool) ? new JapaneseBooleanConverter() : base.Converter;
}

sealed class JapaneseBooleanConverter : BooleanConverter
{
    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) => destinationType == typeof(string) && value is bool b ? b ? "有効" : "無効" : base.ConvertTo(context, culture, value, destinationType);
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) => value is string s && s is "有効" or "無効" ? s == "有効" : base.ConvertFrom(context, culture, value);
}
sealed class ChoiceConverter(string[] choices) : StringConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) => new(choices);
}

sealed class InspectorObject : ICustomTypeDescriptor
{
    readonly List<PropertyDescriptor> properties = new();
    public InspectorObject(GameObject obj, SceneData scene, ScriptCompiler scripts, Action before, Action after)
    {
        void Add(string key, string name, string category, Type type, Func<object?> read, Action<object?> write, string hint = "", string[]? choices = null)
            => properties.Add(new DynamicProperty(key, name, category, type, read, v => { before(); write(v); after(); }, hint, choices));
        Add("Name", "名前", "01 オブジェクト", typeof(string), () => obj.Name, v => obj.Name = Convert.ToString(v) ?? "");
        Add("Active", "有効", "01 オブジェクト", typeof(bool), () => obj.Active, v => obj.Active = Convert.ToBoolean(v));
        Add("Order", "描画順", "01 オブジェクト", typeof(int), () => obj.Order, v => obj.Order = Convert.ToInt32(v), "大きい値ほど手前に表示されます。");
        foreach (var (key, label) in new[] { ("X", "位置 X"), ("Y", "位置 Y"), ("Rotation", "回転（度）"), ("ScaleX", "拡大率 X"), ("ScaleY", "拡大率 Y"), ("Width", "幅"), ("Height", "高さ") })
        {
            var property = typeof(Transform2D).GetProperty(key)!;
            Add("Transform." + key, label, "02 トランスフォーム", typeof(float), () => property.GetValue(obj.Transform), v => { var n = Convert.ToSingle(v); if (!float.IsFinite(n) || Math.Abs(n) > 1000000) throw new ArgumentException("有限の数値（±1000000以内）を指定してください。"); property.SetValue(obj.Transform, n); }, "親オブジェクトからの相対値。原点は左上、Yは下向きです。");
        }
        for (int i = 0; i < obj.Components.Count; i++)
        {
            var c = obj.Components[i]; var def = ComponentsCatalog.All.GetValueOrDefault(c.Type); var category = $"{i + 3:D2} {def?.Label ?? c.Type}";
            Add(c.Id + ".enabled", "有効", category, typeof(bool), () => c.Enabled, v => c.Enabled = Convert.ToBoolean(v), def?.Description ?? "");
            foreach (var f in def?.Fields ?? [])
            {
                var type = f.Kind == "bool" ? typeof(bool) : f.Kind == "number" ? typeof(float) : f.Kind == "int" ? typeof(int) : f.Kind == "color" ? typeof(Color) : typeof(string);
                Add(c.Id + "." + f.Key, f.Label, category, type,
                    () => type == typeof(Color) ? SceneRenderer.ParseColor(c.Get(f.Key, f.Default)) : type == typeof(bool) ? c.Flag(f.Key) : type == typeof(float) ? c.Number(f.Key) : type == typeof(int) ? c.Integer(f.Key) : c.Get(f.Key, f.Default),
                    v => { if (v is float number && !float.IsFinite(number)) throw new ArgumentException("数値を指定してください。"); c.Set(f.Key, v is Color color ? "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2") : v ?? ""); },
                    f.Hint.Length > 0 ? f.Hint : def!.Description, c.Type == "Script" && f.Key == "Class" ? scripts.Types.Keys.ToArray() : f.Choices);
            }
            if (c.Type == "Script" && scripts.Types.TryGetValue(c.Get("Class"), out var scriptType))
            {
                object? defaults = null;
                try { defaults = Activator.CreateInstance(scriptType); } catch { }
                foreach (var field in ScriptCompiler.Fields(scriptType))
                {
                    var attr = field.GetCustomAttribute<ExposeAttribute>(); var key = "field:" + field.Name;
                    var fallback = Convert.ToString(defaults == null ? null : field.GetValue(defaults), CultureInfo.InvariantCulture) ?? "";
                    Add(c.Id + "." + key, attr?.Label ?? field.Name, category, field.FieldType.IsEnum ? typeof(string) : field.FieldType,
                        () => field.FieldType.IsEnum ? c.Get(key, fallback) : ScriptCompiler.Parse(c.Get(key, fallback), field.FieldType),
                        v => c.Set(key, v ?? ""), attr?.Description ?? "C#の公開フィールド", field.FieldType.IsEnum ? Enum.GetNames(field.FieldType) : null);
                }
            }
        }
    }
    public AttributeCollection GetAttributes() => AttributeCollection.Empty;
    public string? GetClassName() => null;
    public string? GetComponentName() => null;
    public TypeConverter GetConverter() => new();
    public EventDescriptor? GetDefaultEvent() => null;
    public PropertyDescriptor? GetDefaultProperty() => null;
    public object? GetEditor(Type editorBaseType) => null;
    public EventDescriptorCollection GetEvents() => EventDescriptorCollection.Empty;
    public EventDescriptorCollection GetEvents(Attribute[]? attributes) => GetEvents();
    public PropertyDescriptorCollection GetProperties() => new(properties.ToArray());
    public PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => GetProperties();
    public object GetPropertyOwner(PropertyDescriptor? pd) => this;
}
