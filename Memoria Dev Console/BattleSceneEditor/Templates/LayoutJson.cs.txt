using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Small, typed JSON document codec for Unity's installed .NET subset. No
// external JSON assembly, XML/Data dependency, reflection, or type activation.
namespace Memoria.BattleSceneLayouts.Json
{
    public enum Formatting { None, Indented }
    public enum JTokenType { Object, Array, Integer, Float, String, Boolean, Null }
    public abstract class JToken : IEnumerable<JToken>
    {
        internal JToken Parent;
        public abstract JTokenType Type { get; }
        public virtual JToken this[String key] { get { throw new InvalidOperationException(); } set { throw new InvalidOperationException(); } }
        public virtual JToken this[Int32 index] { get { throw new InvalidOperationException(); } set { throw new InvalidOperationException(); } }
        public virtual Int32 Count { get { return 0; } }
        public virtual IEnumerator<JToken> GetEnumerator() { yield break; }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
        public void Remove() { if (Parent is JArray) ((JArray)Parent).RemoveChild(this); else if (Parent is JObject) ((JObject)Parent).RemoveChild(this); Parent=null; }
        public JToken DeepClone() { return Parser.Parse(ToString(Formatting.None)); }
        public String ToString(Formatting format) { var s=new StringBuilder(); Write(s,format,0); return s.ToString(); }
        public override String ToString() { return ToString(Formatting.None); }
        internal abstract void Write(StringBuilder s,Formatting format,Int32 depth);
        internal static JToken Wrap(Object value)
        {
            if(value is JToken)return (JToken)value;
            return new JValue(value);
        }
        internal static void Indent(StringBuilder s,Formatting f,Int32 d) { if(f==Formatting.Indented){s.Append('\n');s.Append(' ',d*2);} }
        internal static void Quote(StringBuilder s,String text)
        {
            s.Append('"');
            foreach(Char c in text)
            {
                switch(c){case '"':s.Append("\\\"");break;case '\\':s.Append("\\\\");break;case '\b':s.Append("\\b");break;case '\f':s.Append("\\f");break;case '\n':s.Append("\\n");break;case '\r':s.Append("\\r");break;case '\t':s.Append("\\t");break;
                default: if(c<32 || Char.IsSurrogate(c))s.Append("\\u").Append(((Int32)c).ToString("x4",CultureInfo.InvariantCulture));else s.Append(c);break;}
            }
            s.Append('"');
        }
        public static implicit operator JToken(String v){return new JValue(v);}
        public static implicit operator JToken(Int32 v){return new JValue(v);}
        public static implicit operator JToken(Int16 v){return new JValue(v);}
        public static implicit operator JToken(Single v){return new JValue(v);}
        public static implicit operator JToken(Double v){return new JValue(v);}
        public static implicit operator JToken(Boolean v){return new JValue(v);}
        public static explicit operator String(JToken v){return v==null||v.Type==JTokenType.Null?null:v.ToString();}
        public static explicit operator Int32(JToken v){return Int32.Parse(v.ToString(),NumberStyles.Integer,CultureInfo.InvariantCulture);}
        public static explicit operator Int16(JToken v){return checked((Int16)(Int32)v);}
        public static explicit operator Single(JToken v){return Single.Parse(v.ToString(),NumberStyles.Float,CultureInfo.InvariantCulture);}
        public static explicit operator Double(JToken v){return Double.Parse(v.ToString(),NumberStyles.Float,CultureInfo.InvariantCulture);}
        public static explicit operator Boolean(JToken v){if(v==null||v.Type!=JTokenType.Boolean)throw new FormatException("Expected JSON boolean.");return Boolean.Parse(v.ToString());}
        public static explicit operator Int32?(JToken v){return v==null||v.Type==JTokenType.Null?(Int32?)null:(Int32)v;}
        public static explicit operator Int16?(JToken v){return v==null||v.Type==JTokenType.Null?(Int16?)null:(Int16)v;}
        public static explicit operator Boolean?(JToken v){return v==null||v.Type==JTokenType.Null?(Boolean?)null:(Boolean)v;}
    }
    public sealed class JValue : JToken
    {
        private readonly Object value;
        private readonly JTokenType type;
        internal readonly String RawNumber;
        public JValue(Object value)
        {
            this.value=value;
            type=value==null?JTokenType.Null:value is String?JTokenType.String:value is Boolean?JTokenType.Boolean:
                value is Single||value is Double||value is Decimal?JTokenType.Float:JTokenType.Integer;
            if(value!=null && !(value is String||value is Boolean||value is IConvertible))throw new ArgumentException("Unsupported JSON scalar.");
        }
        internal JValue(String number,Boolean floating){RawNumber=number;type=floating?JTokenType.Float:JTokenType.Integer;}
        public static JValue CreateNull(){return new JValue(null);}
        public override JTokenType Type {get{return type;}}
        public override String ToString()
        {
            if(RawNumber!=null)return RawNumber;
            if(type==JTokenType.Null)return "";
            if(type==JTokenType.Boolean)return (Boolean)value?"true":"false";
            if(value is Single)return ((Single)value).ToString("R",CultureInfo.InvariantCulture);
            if(value is Double)return ((Double)value).ToString("R",CultureInfo.InvariantCulture);
            return Convert.ToString(value,CultureInfo.InvariantCulture);
        }
        internal override void Write(StringBuilder s,Formatting f,Int32 d)
        {
            if(type==JTokenType.String)Quote(s,(String)value);
            else if(type==JTokenType.Null)s.Append("null");
            else {String text=ToString();if(text=="NaN"||text=="Infinity"||text=="-Infinity")throw new FormatException("Nonfinite JSON number.");s.Append(text);}
        }
    }
    public sealed class JProperty
    {
        public readonly String Name;
        public JToken Value;
        public JProperty(String name,Object value){Name=name;Value=JToken.Wrap(value);}
    }
    public sealed class JObject : JToken
    {
        private readonly List<JProperty> properties=new List<JProperty>();
        public JObject(params JProperty[] values){foreach(var p in values)this[p.Name]=p.Value;}
        public static JObject Parse(String json){var result=Parser.Parse(json) as JObject;if(result==null)throw new FormatException("Expected JSON object.");return result;}
        public override JTokenType Type {get{return JTokenType.Object;}}
        public override Int32 Count {get{return properties.Count;}}
        public override JToken this[String key]
        {
            get{foreach(var p in properties)if(p.Name==key)return p.Value;return null;}
            set{JToken token=value??JValue.CreateNull();token.Parent=this;foreach(var p in properties)if(p.Name==key){p.Value=token;return;}properties.Add(new JProperty(key,token));}
        }
        public IEnumerable<JProperty> Properties(){return properties;}
        public override IEnumerator<JToken> GetEnumerator(){foreach(var p in properties)yield return p.Value;}
        internal void RemoveChild(JToken child){for(Int32 i=0;i<properties.Count;i++)if(Object.ReferenceEquals(properties[i].Value,child)){properties.RemoveAt(i);return;}}
        internal override void Write(StringBuilder s,Formatting f,Int32 depth)
        {
            if(depth>64)throw new FormatException("JSON nesting limit exceeded.");s.Append('{');Int32 i=0;
            foreach(var p in properties){if(i++>0)s.Append(',');Indent(s,f,depth+1);Quote(s,p.Name);s.Append(f==Formatting.Indented?": ":":");p.Value.Write(s,f,depth+1);}
            if(properties.Count>0)Indent(s,f,depth);s.Append('}');
        }
    }
    public sealed class JArray : JToken
    {
        private readonly List<JToken> values=new List<JToken>();
        public JArray(params Object[] items){foreach(var item in items)Add(item);}
        public void Add(Object value){JToken token=Wrap(value);token.Parent=this;values.Add(token);}
        public override JTokenType Type {get{return JTokenType.Array;}}
        public override Int32 Count {get{return values.Count;}}
        public override JToken this[Int32 index]{get{return values[index];}set{var token=value??JValue.CreateNull();token.Parent=this;values[index]=token;}}
        public override IEnumerator<JToken> GetEnumerator(){return values.GetEnumerator();}
        internal void RemoveChild(JToken child){values.Remove(child);}
        internal override void Write(StringBuilder s,Formatting f,Int32 depth)
        {
            if(depth>64)throw new FormatException("JSON nesting limit exceeded.");s.Append('[');
            for(Int32 i=0;i<values.Count;i++){if(i>0)s.Append(',');Indent(s,f,depth+1);values[i].Write(s,f,depth+1);}if(values.Count>0)Indent(s,f,depth);s.Append(']');
        }
    }
    internal sealed class Parser
    {
        private readonly String text;private Int32 index;
        private Parser(String text){this.text=text??"";}
        internal static JToken Parse(String text){var p=new Parser(text);JToken result=p.Value(0);p.Space();if(p.index!=p.text.Length)throw p.Error();return result;}
        private FormatException Error(){return new FormatException("Invalid JSON at character "+index);}
        private void Space(){while(index<text.Length && (text[index]==' '||text[index]=='\r'||text[index]=='\n'||text[index]=='\t'))index++;}
        private Boolean Eat(Char c){Space();if(index<text.Length&&text[index]==c){index++;return true;}return false;}
        private JToken Value(Int32 depth)
        {
            if(depth>64)throw Error();Space();if(index>=text.Length)throw Error();Char c=text[index];
            if(c=='{')
            {
                index++;var o=new JObject();if(Eat('}'))return o;
                do{Space();String key=String();if(o[key]!=null||!Eat(':'))throw Error();o[key]=Value(depth+1);if(Eat('}'))return o;}while(Eat(','));throw Error();
            }
            if(c=='['){index++;var a=new JArray();if(Eat(']'))return a;do{a.Add(Value(depth+1));if(Eat(']'))return a;}while(Eat(','));throw Error();}
            if(c=='"')return new JValue(String());
            if(c=='t'){Literal("true");return new JValue(true);}if(c=='f'){Literal("false");return new JValue(false);}if(c=='n'){Literal("null");return JValue.CreateNull();}
            Int32 begin=index;if(c=='-')index++;if(index>=text.Length)throw Error();
            if(text[index]=='0')index++;else{if(text[index]<'1'||text[index]>'9')throw Error();while(index<text.Length&&Digit(text[index]))index++;}
            Boolean floating=false;
            if(index<text.Length&&text[index]=='.'){floating=true;index++;Int32 start=index;while(index<text.Length&&Digit(text[index]))index++;if(start==index)throw Error();}
            if(index<text.Length&&(text[index]=='e'||text[index]=='E')){floating=true;index++;if(index<text.Length&&(text[index]=='+'||text[index]=='-'))index++;Int32 start=index;while(index<text.Length&&Digit(text[index]))index++;if(start==index)throw Error();}
            return new JValue(text.Substring(begin,index-begin),floating);
        }
        private static Boolean Digit(Char c){return c>='0'&&c<='9';}
        private void Literal(String value){if(index+value.Length>text.Length||text.Substring(index,value.Length)!=value)throw Error();index+=value.Length;}
        private String String()
        {
            if(index>=text.Length||text[index++]!='"')throw Error();var result=new StringBuilder();
            while(index<text.Length)
            {
                Char c=text[index++];if(c=='"')return result.ToString();if(c<32)throw Error();if(c!='\\'){result.Append(c);continue;}
                if(index>=text.Length)throw Error();c=text[index++];
                switch(c){case '"':case '\\':case '/':result.Append(c);break;case 'b':result.Append('\b');break;case 'f':result.Append('\f');break;case 'n':result.Append('\n');break;case 'r':result.Append('\r');break;case 't':result.Append('\t');break;case 'u':
                    if(index+4>text.Length)throw Error();Int32 value;if(!Int32.TryParse(text.Substring(index,4),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out value))throw Error();result.Append((Char)value);index+=4;break;
                    default:throw Error();}
            }
            throw Error();
        }
    }
}
