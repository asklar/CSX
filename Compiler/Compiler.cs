using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CSharp;
using Microsoft.Toolkit.Forms.UI.XamlHost;
using Windows.UI.Xaml;

namespace csxc2
{
    static class XamlTypeUtils
    {
        public static Type GetXamlType (string typename)
        {
            return Type.GetType ($"Windows.UI.Xaml.Controls.{typename}, Windows.UI.Xaml, Version=255.255.255.255, Culture=neutral, PublicKeyToken=null, ContentType=WindowsRuntime");
        }

        public static void ParentXaml (Windows.UI.Xaml.UIElement root)
        {
            var host = new WindowsXamlHost ();
            host.Child = root;
        }
    }
    class Program
    {
        class ParseState
        {

            private enum ParseMode
            {
                CSharp = 0,
                LineComment,
                BlockComment,
                String,
                XML,
                XMLComment
            }

            private DateTime startTime = DateTime.Now;

            private static int s_id = 0;
            private int id = s_id++;
            public ParseState (string source)
            {
                Source = source;
            }
            public string Source { get; set; }
            public StreamWriter Output { get; set; } = new StreamWriter (new MemoryStream ());
            private ParseMode Mode { get; set; } = ParseMode.CSharp;
            public int CurrentPosition { get; set; } = 0;
            private Stack<XElement> xmlElements = new Stack<XElement> ();

            public int CurrentLineNumber { get; set; } = 0;

            private Dictionary<string, string> names = null;

            private string GetGeneratedObjectType (string type, bool isTopElement)
            {
                if (isTopElement)
                {
                    return $"TopLevelObject<{type}>";
                }
                else
                {
                    return type;
                }
            }
            private string GetXmlElementSyntax (XElement element, Type typeOfChildrenCollectionItems, bool isTopElement = false)
            {

                string ret = $"new {GetGeneratedObjectType(element.Name.LocalName, isTopElement)}()";
                List<string> initializer = new List<string> ();

                var nonChildren = from e in element.Elements ()
                where (!IsChildableElement (e, typeOfChildrenCollectionItems))
                select e;

                var attributeInitializer = from a in element.Attributes ()
                where a.Name.LocalName != "Id" && a.Name.LocalName != "ContentExpression"
                select $"{AsTypeOfProperty(element.Name.LocalName, a.Name.LocalName, GetAttributeValueString(a.Value))};";
                var eventHandlerInitializer = from e in element.Elements ()
                where (e.Attribute ("Handler") != null)
                select $"_this.{GetLastSegment(e.Name.LocalName)} += ({GetHandlerParams(e.Name.LocalName, element.Name.LocalName)}) => {{{GetAttributeValueString(e.Attribute("Handler").Value)}}};";
                var collectionInitializer = from e in nonChildren
                where e.Attribute ("Handler") == null
                select GetCollectionInitializer (e);
                var idValue = element.Attribute ("Id")?.Value;
                if (idValue != null)
                {
                    names[idValue] = GetLastSegment (element.Name.LocalName);
                    //initializer.Add($"Id = {GetAttributeValueString(idValue)}");
                }

                List<string> contentExpressionInitializer = new List<string> ();
                var contentExpression = element.Attribute ("ContentExpression")?.Value;
                if (contentExpression != null)
                {
                    contentExpressionInitializer.Add ($"Func<IEnumerable<{typeOfChildrenCollectionItems.Name}>> contentExpression = () => {contentExpression};");
                    contentExpressionInitializer.Add ($"foreach (var el in contentExpression()) {{ _this.Children.Add(el); }}");
                }

                var instanceInitializers = new List<string> ();

                if (isTopElement)
                {
                    instanceInitializers.Add ($"var _TopLevelElement = _wrapper;");
                }
                if (idValue != null)
                {
                    instanceInitializers.Add ($"var {idValue} = _this;");
                    instanceInitializers.Add ($"_TopLevelElement.Objects[\"{idValue}\"] = {idValue};");
                }
                if (element.HasElements)
                {
                    var elements = string.Join (", ", from e in element.Elements () where (IsChildableElement (e, typeOfChildrenCollectionItems)) select GetXmlElementSyntax (e, typeOfChildrenCollectionItems));
                    instanceInitializers.Add ($"var elements = new List<{typeOfChildrenCollectionItems.Name}>() {{{elements}}};");
                    instanceInitializers.Add ($"foreach (var el in elements) {{ _this.Children.Add(el); }}");
                }

                instanceInitializers.AddRange (collectionInitializer);
                instanceInitializers.AddRange (attributeInitializer);
                instanceInitializers.AddRange (eventHandlerInitializer);
                instanceInitializers.AddRange (contentExpressionInitializer);

                string andThenParams = isTopElement ? "(_this, _wrapper)" : "(_this)";
                var instanceInitializer = instanceInitializers.Count != 0 ?
                    $".AndThen({andThenParams} => {{ " +
                        $"{string.Join(Environment.NewLine, instanceInitializers)}" +
                        $"}})" : "";
                initializer.Add (instanceInitializer);
                ret += string.Join (", ", initializer);
                if (isTopElement)
                {
                    return $"({ret}).Instance";
                }
                else
                {
                    return $"({ret})";
                }

            }

            private string GetHandlerParams (string propertyName, string type)
            {
                propertyName = GetLastSegment (propertyName);
                Type xamltype = XamlTypeUtils.GetXamlType (type);
                if (xamltype == null)
                {
                    throw new Exception ($"Xaml type {type} not found");
                }
                var eventinfo = xamltype.GetEvent (propertyName);
                if (eventinfo == null)
                {
                    throw new Exception ($"Event handler {propertyName} not found in type {type}");
                }
                var eventparams = eventinfo.EventHandlerType.GetMethod ("Invoke")?.GetParameters ();
                if (eventparams == null)
                {
                    throw new Exception ($"Couldn't determine event handler parameter types for event {propertyName} in type {type}");
                }
                return string.Join (", ", eventparams.Select ((p) => p.Name));
            }

            private string GetCollectionInitializer (XElement e)
            {
                if (e.HasAttributes)
                {
                    throw new Exception ($"Collection element {e.Name} has unexpected attributes");
                }
                var propertyName = GetLastSegment (e.Name.LocalName);
                List<string> initializer = new List<string> ();
                foreach (var child in e.Elements ())
                {
                    var childInitializer = GetXmlElementSyntax (child, typeof (Windows.UI.Xaml.UIElement));
                    initializer.Add ($"_this.{propertyName}.Add({childInitializer});");
                }
                return string.Join (Environment.NewLine, initializer);
            }

            private string AsTypeOfProperty (string element, string propertyName, string value)
            {
                string attributeInitializerLValue = $"_this.{GetLastSegment(propertyName)}";
                if (value[0] != '"' || value[value.Length - 1] != '"')
                {
                    // the value is not a literal. Assume that things are correctly type, 
                    // worst-case the C# compiler will complain
                    return $"{attributeInitializerLValue} = {value};";
                }
                else if (propertyName == "Id")
                {
                    return value; // this is a fake property, we'll figure x:ref stuff later
                }
                string rawValue = value.Substring (1, value.Length - 2);
                var type = XamlTypeUtils.GetXamlType (element);
                if (type == null)
                {
                    throw new Exception ($"Xaml type {element} not found");
                }
                var propinfo = type.GetProperty (propertyName);
                if (propinfo == null)
                {
                    // this isn't a property on the type. Maybe it's an attached property
                    if (propertyName.IndexOf ('.') != -1)
                    {
                        element = propertyName.Substring (0, propertyName.LastIndexOf ('.'));
                        propertyName = GetLastSegment (propertyName);
                        type = XamlTypeUtils.GetXamlType (element);
                        if (type == null)
                        {
                            throw new Exception ($"Attribute {propertyName} looks like an attached property but {element} isn't a recognized type");
                        }
                        else
                        {
                            var setter = type.GetMethod ($"Set{propertyName}");
                            // The syntax for attached properties is:
                            // theParentElement.SetFoo(theCurrentElement, theValue);
                            if (setter != null && setter.IsStatic && setter.GetParameters ().Length == 2)
                            {
                                var typeOfParameter = setter.GetParameters () [1].ParameterType;
                                return $"{element}.{setter.Name}(_this, {AsType(rawValue, typeOfParameter, propertyName)});";
                            }

                        }
                    }
                    throw new Exception ($"Property {propertyName} not found in type {element}");
                }
                var proptype = propinfo.PropertyType;
                return $"{attributeInitializerLValue} = {AsType(rawValue, proptype, element)};";
            }

            private string AsType (string rawValue, Type proptype, string inType)
            {
                if (proptype.IsAssignableFrom (typeof (string)))
                {
                    return $"\"{rawValue}\"";
                }
                else if (proptype.IsAssignableFrom (typeof (double)))
                {
                    return $"{double.Parse(rawValue)}";
                }
                else if (proptype.IsAssignableFrom (typeof (int)))
                {
                    return $"{int.Parse(rawValue)}";
                }
                else if (proptype.IsEnum)
                {
                    return $"{proptype}.{rawValue}";
                }
                else if (proptype.IsAssignableFrom (typeof (Windows.UI.Xaml.GridLength)))
                {
                    GridUnitType gridUnitType = GridUnitType.Auto;
                    if (rawValue.EndsWith ("*"))
                    {
                        gridUnitType = GridUnitType.Star;
                        rawValue = rawValue.Substring (0, rawValue.Length - 1);
                    }
                    int px = rawValue == "" ? 1 : int.Parse (rawValue);
                    return $"new Windows.UI.Xaml.GridLength({px}, Windows.UI.Xaml.GridUnitType.{gridUnitType})";
                }
                else
                {
                    throw new Exception ($"don't know how to interpret a value of type {proptype.Name} in element type {inType}. Value = {rawValue}");
                }
            }

            private string GetLastSegment (string localName)
            {
                return localName.Substring (localName.LastIndexOf ('.') + 1);
            }

            private string GetAttributeValueString (string value)
            {
                if (value.Length == 0) { return value; }
                if (value[0] == '{')
                {
                    // this is an expression, remove the braces
                    value = value.Substring (1, value.Length - 2);
                    // now we need to replace any reference to named objects with a lookup in the top level name service
                    if (names != null)
                    {
                        foreach (var name in names.Keys)
                        {
                            value = value.Replace (name, $"(_TopLevelElement.Objects[\"{name}\"] as {names[name]})");
                        }
                    }
                }
                else
                {
                    value = '"' + value + '"';
                }

                return value;
            }

            private bool IsChildableElement (XElement e, Type typeOfChildrenCollectionItems)
            {
                if (e.Attribute ("Handler") != null)
                {
                    return false; // this is an event handler, not a ui element
                }
                var localname = e.Name.LocalName;
                var finalname = GetLastSegment (localname);
                if (finalname.EndsWith ('s'))
                {
                    // this is probably like "RowDefinitions" so the type is "RowDefinitionCollection"
                    finalname = finalname.Substring (0, finalname.Length - 1) + "Collection";
                }
                var xamltype = XamlTypeUtils.GetXamlType (finalname);
                if (xamltype == null)
                {
                    throw new Exception ($"type {xamltype} not found");
                }
                if (typeOfChildrenCollectionItems.IsAssignableFrom (xamltype))
                {
                    return true;
                }
                return false;
            }

            private string MapEventHandlerName (string name)
            {
                return "Handler_" + name.Replace ('.', '_');
            }

            private string SimpleFormatXmlElementString (string str)
            {
                str = Regex.Replace (str, ";+", ";");
                bool isInInterpolatedString = false;
                string s = "";
                int indent = 0;
                bool shouldTrimWhitespace = true;
                for (int i = 0; i < str.Length; i++)
                {
                    while (shouldTrimWhitespace && i < str.Length && char.IsWhiteSpace (str[i]))
                    {
                        i++;
                    }
                    if (i == str.Length) { break; }
                    s += str[i];
                    if (str[i] == '$' && i + 1 < str.Length && str[i + 1] == '"')
                    {
                        isInInterpolatedString = true;
                    }
                    else if (str[i] == '}' && !isInInterpolatedString)
                    {
                        indent--;
                        if (i + 1 < str.Length && str[i + 1] == ',')
                        {
                            s += str[++i];
                        }
                        s += Environment.NewLine + new string (' ', indent * 4);
                        shouldTrimWhitespace = true;
                    }
                    else if (str[i] == '{' && !isInInterpolatedString)
                    {
                        indent++;
                        if (i + Environment.NewLine.Length < str.Length && str.Substring (i + 1).StartsWith (Environment.NewLine))
                        {
                            i += Environment.NewLine.Length;
                        }
                        s += Environment.NewLine + new string (' ', indent * 4);
                        shouldTrimWhitespace = true;
                    }
                    else if (str[i] == ',' || str[i] == ';')
                    {
                        s += Environment.NewLine + new string (' ', indent * 4);
                        shouldTrimWhitespace = true;
                    }
                    else
                    {
                        shouldTrimWhitespace = false;
                        if (isInInterpolatedString && str.Substring (i).StartsWith (Environment.NewLine))
                        {
                            isInInterpolatedString = false;
                        }
                    }
                }
                return s;
            }
            private void PrintXmlElement (XElement element)
            {
                //PrintLine(element.ToString());
                names = new Dictionary<string, string> ();
                PrintLine (SimpleFormatXmlElementString (GetXmlElementSyntax (element, typeof (Windows.UI.Xaml.UIElement), true)));
                names = null;
            }
            private void EndXmlElement (string expectedElement)
            {
                var pop = xmlElements.Pop ();
                if (expectedElement != pop.Name)
                {
                    throw new ArgumentException ($"xml tag close error, expected {expectedElement}");
                }

                Debug.WriteLine ($"ended element {expectedElement}");
                if (xmlElements.Count == 0)
                {
                    PrintXmlElement (pop);
                    Mode = ParseMode.CSharp;
                }
                else
                {
                    XElement top = xmlElements.Peek ();
                    top.Add (pop);
                    Mode = ParseMode.XML;
                }
            }

            private string[] imports = new string[]
            {
                "System",
                "Windows.UI.Xaml",
                "Windows.UI.Xaml.Controls",
                "Windows.UI.Xaml.Media",
                "System.Collections.Generic",
                "CSX"
            };
            public void Parse ()
            {
                Debug.WriteLine ($"Started parse ID {id}");
                Debug.WriteLine (Source);
                PrintLine (string.Join (Environment.NewLine, imports.Select (e => $"using {e};")));

                while (CurrentPosition < Source.Length)
                {
                    ParseInContext ();
                }
                if (xmlElements.Count != 0)
                {
                    throw new ArgumentException ($"xml element wasn't closed {xmlElements.Peek()}");
                }
                Debug.WriteLine ($"Ended parse ID {id}");
            }

            private int SkipToAfter (string source, string substr)
            {
                int i = source.IndexOf (substr);
                if (i == -1)
                {
                    throw new ArgumentException ($"expected {substr}");
                }
                return i + substr.Length;
            }

            int FindBalancedEnd (string source)
            {
                Debug.Assert (source[0] == '{');
                int needClosing = 1;
                for (int i = 1; i < source.Length; i++)
                {
                    if (source[i] == '}')
                    {
                        if (needClosing > 0)
                        {
                            needClosing--;
                        }
                        else
                        {
                            throw new ArgumentException ("unbalanced parens");
                        }

                        if (needClosing == 0)
                        {
                            return i;
                        }
                    }
                    else if (source[i] == '{')
                    {
                        needClosing++;
                    }
                }
                throw new ArgumentException ("unbalanced parens");
            }
            private bool XmlContentIsAllowed = false;

            private const string valueExpression = @"(({(?<eattrvalue>(?:[^{}]|(?<open>{)|(?<-open>}))+(?(open)(?!)))}(?<sattrvalue>))|(""(?<sattrvalue>[^""]*)""(?<eattrvalue>)))";
            private Regex xmlStart = new Regex (@"^<\s*(?<elementname>\w+(\.\w+)*)\s*(?<attrlist>(?<attrname>(\w+(\.\w+)*))\s*=\s*" + valueExpression + @"\s*)*(?<endelement>/)?>", RegexOptions.Compiled);
            private Regex xmlEnd = new Regex (@"^</\s*(?<elementname>\w+(\.\w+)*)\s*>");

            enum AttributeValueKind
            {
                String,
                Expression
            }

            private double Timestamp { get => (DateTime.Now - startTime).TotalMilliseconds; }
            public void ParseInContext ()
            {
                //PrintLine(CurrentLineNumber, $"ParseInContext @ {CurrentPosition} {Mode.ToString()} {Timestamp}");
                string source = Source.Substring (CurrentPosition);
                int nextPosition = CurrentPosition;
                if (source.StartsWith (Environment.NewLine))
                {
                    Debug.WriteLine ("newline");
                    CurrentLineNumber++;
                    CurrentPosition += Environment.NewLine.Length;
                    PrintLine (Environment.NewLine);
                    return;
                }
                switch (Mode)
                {
                    case ParseMode.CSharp:
                        {
                            if (source.StartsWith ("//"))
                            {
                                Mode = ParseMode.LineComment;
                            }
                            else if (source.StartsWith ("/*"))
                            {
                                Mode = ParseMode.BlockComment;
                            }
                            else if (source.StartsWith ('"'))
                            {
                                Mode = ParseMode.String;
                                nextPosition++;
                            }
                            else if (xmlStart.IsMatch (source) && !IsContextGenericType ())
                            {
                                Mode = ParseMode.XML;
                            }
                            else
                            {
                                PrintLine (source[0].ToString ());
                                Debug.Flush ();
                                nextPosition++;
                            }
                        }
                        break;
                    case ParseMode.LineComment:
                        {
                            nextPosition += source.IndexOf (Environment.NewLine);
                            var text = source.Substring (0, nextPosition - CurrentPosition);
                            PrintLine (text);
                            Debug.WriteLine ($"comment {text}");
                            Mode = ParseMode.CSharp;
                        }
                        break;
                    case ParseMode.BlockComment:
                        {
                            Debug.WriteLine ("block comment");
                            nextPosition += SkipToAfter (source, "*/");
                            var text = source.Substring (0, nextPosition - CurrentPosition);
                            PrintLine (text);
                            Mode = ParseMode.CSharp;
                        }
                        break;
                    case ParseMode.String:
                        {
                            /// TODO: deal with strings with embedded quotes, etc.
                            nextPosition += SkipToAfter (source, @"""");
                            // does not include "" marks
                            var text = source.Substring (0, nextPosition - CurrentPosition - 1);
                            PrintLine ($"\"{text}\"");
                            Debug.WriteLine ($"string {text}");
                            Mode = ParseMode.CSharp;
                        }
                        break;
                    case ParseMode.XML:
                        {
                            if (source.StartsWith ("<!--"))
                            {
                                nextPosition += SkipToAfter (source, "-->");
                                var text = source.Substring (0, nextPosition - CurrentPosition);
                                PrintLine ($"/* {text} */");
                                Debug.WriteLine ($"xml comment {text}");
                                break;
                            }
                            var match = xmlStart.Match (source);
                            if (match.Success)
                            {
                                var name = match.Groups["elementname"].Value;

                                var element = new XElement (name);
                                Debug.WriteLine ($"found an XML element {name}; {match.Groups["attrname"].Captures.Count} attributes");
                                for (int i = 0; i < match.Groups["attrlist"].Captures.Count; i++)
                                {
                                    var attributeValueKind = (match.Groups["eattrvalue"].Captures[i].Value == "") ? AttributeValueKind.String : AttributeValueKind.Expression;
                                    var attrname = match.Groups["attrname"].Captures[i].Value;
                                    var value = (attributeValueKind == AttributeValueKind.String) ? match.Groups["sattrvalue"].Captures[i].Value : match.Groups["eattrvalue"].Captures[i].Value;
                                    Debug.WriteLine ($"    {attrname} ---> {value} ({attributeValueKind})");
                                    var attrValue = (attributeValueKind == AttributeValueKind.Expression) ? $"{{{value}}}" : value;
                                    element.SetAttributeValue (attrname, attrValue);
                                }
                                xmlElements.Push (element);
                                if (match.Groups["endelement"].Success)
                                {
                                    EndXmlElement (name);
                                }
                                else
                                { }
                                nextPosition += match.Length;
                            }
                            else
                            {
                                match = xmlEnd.Match (source);
                                if (match.Success)
                                {
                                    var name = match.Groups["elementname"].Value;
                                    EndXmlElement (name);
                                    nextPosition += match.Length;
                                }
                                else
                                {
                                    if (source.StartsWith (Environment.NewLine))
                                    {
                                        Debug.WriteLine ("NEWLINE");
                                    }
                                    else if (char.IsWhiteSpace (source[0]))
                                    {

                                    }
                                    else if (source[0] == '{')
                                    {
                                        nextPosition += FindBalancedEnd (source) + 1;
                                        var expression = source.Substring (0, nextPosition - CurrentPosition);
                                        var currentXMLElement = xmlElements.Peek ();
                                        Debug.WriteLine ($"[{currentXMLElement.Name}] content expression initializer: {expression}");
                                        /// TODO: A content expression can itself have XML elements. For now, ignore that
                                        // ParseState nested = new ParseState(expression);
                                        // nested.CurrentLineNumber = CurrentLineNumber;
                                        // nested.Parse();
                                        // Console.WriteLine("END NESTED");
                                        /// END TODO
                                        if (currentXMLElement.Attribute ("ContentExpression") != null)
                                        {
                                            throw new Exception ($"Element {currentXMLElement.Name} already has a content expression");
                                        }
                                        currentXMLElement.SetAttributeValue ("ContentExpression", expression);
                                        if (nextPosition < Source.Length)
                                        {
                                            Debug.WriteLine ($"remainder is {Source.Substring(nextPosition)}");
                                        }
                                        else
                                        {
                                            Debug.WriteLine ("EOF");
                                        }
                                        break;
                                    }
                                    else if (XmlContentIsAllowed)
                                    {
                                        Debug.WriteLine ($"xml content: {source[0]}");
                                    }
                                    else
                                    {
                                        throw new ArgumentException ($"content not allowed inside XML tag {xmlElements.Peek().Name}: {source} in line {CurrentLineNumber + 1} ");
                                    }
                                    nextPosition++;
                                }
                            }
                        }
                        break;
                }
                CurrentLineNumber += CountLines (source.Substring (0, nextPosition - CurrentPosition));
                CurrentPosition = nextPosition;
                Debug.WriteLine ($"exit at {Timestamp}");
            }

            /// True if the text preceding CurrentPosition signifies a generic type
            /// This is used to differentiate when a <T> refers to a generic type parameter or an XML tag
            private bool IsContextGenericType ()
            {
                var startOfLastWord = -1;
                for (int i = CurrentPosition - 1; i > 0; i--)
                {
                    if (char.IsWhiteSpace (Source[i]))
                    {
                        startOfLastWord = i + 1;
                        break;
                    }
                }
                if (startOfLastWord == -1)
                {
                    throw new Exception ("Couldn't determine whether we're dealing with a generic type parameter or an XML tag");
                }
                var typeParameter = Source.Substring (CurrentPosition);
                typeParameter = typeParameter.Substring (1, typeParameter.IndexOf ('>') - 1);
                // short-circuit, generic parameter types don't have spaces in them, 
                // this is probably a space inside an XML tag
                if (typeParameter.IndexOf (' ') != -1) { return false; }
                if (Type.GetType (typeParameter) == null)
                {
                    // this isn't a real .net type, maybe it's a shorthand type
                    if (TypeShorthand.TryGetValue (typeParameter, out Type realTypeParameter))
                    {
                        typeParameter = realTypeParameter.FullName;
                    }
                }
                var possibleGenericTypeName = Source.Substring (startOfLastWord, CurrentPosition - startOfLastWord) + "`1";
                if (Type.GetType (possibleGenericTypeName) == null)
                {
                    if (TypeShorthand.TryGetValue (possibleGenericTypeName, out Type realTypeParameter))
                    {
                        possibleGenericTypeName = realTypeParameter.FullName;
                    }
                }

                string typeName = $"{possibleGenericTypeName}[{typeParameter}]";
                Type possibleType = Type.GetType (typeName);
                if (possibleType != null)
                {
                    return possibleType.IsGenericType;
                }
                else
                {
                    return false;
                }
            }

            private Dictionary<string, Type> typeShorthand = null;
            private Dictionary<string, Type> TypeShorthand
            {
                get
                {
                    if (typeShorthand == null)
                    {
                        typeShorthand = GetTypeShorthandMap ();
                    }
                    return typeShorthand;
                }
            }
            private Dictionary<string, Type> GetTypeShorthandMap ()
            {
                var d = new Dictionary<string, Type> ();
                var mscorlib = Assembly.GetAssembly (typeof (int));
                using (var provider = new CSharpCodeProvider ())
                {
                    foreach (var type in mscorlib.DefinedTypes)
                    {
                        if (string.Equals (type.Namespace, "System"))
                        {
                            var typeRef = new CodeTypeReference (type);
                            var csTypeName = provider.GetTypeOutput (typeRef);

                            // Ignore qualified types.
                            if (csTypeName.IndexOf ('.') == -1)
                            {
                                d[csTypeName] = type;
                            }
                        }
                        else if (type.FullName.EndsWith ("`1"))
                        {
                            // this is a 1-param generic type, we need this to be 
                            // able to disambiguate generics from xml tags
                            d[GetLastSegment (type.FullName)] = type;
                        }
                    }
                }
                return d;

            }

            private int lastPrintedLine = -1;
            private bool lineIsWhitespace = true;
            private void PrintLine (string text)
            {
                if (CurrentLineNumber == lastPrintedLine + 1)
                {
                    if (lastPrintedLine > -1 && !lineIsWhitespace)
                    {
                        Output.Write (Environment.NewLine);
                        lineIsWhitespace = true;
                    }
                    lastPrintedLine = CurrentLineNumber;
                    if (text.StartsWith (Environment.NewLine))
                    {
                        text = text.Substring (Environment.NewLine.Length);
                    }
                }
                else if (CurrentLineNumber != lastPrintedLine)
                {
                    if (!lineIsWhitespace)
                    {
                        Output.Write (Environment.NewLine);
                        /// TODO: reenable once it's more useful
                        /// Output.WriteLine($"#line {CurrentLineNumber + 1}");
                    }
                    lastPrintedLine = CurrentLineNumber;
                    lineIsWhitespace = true;
                    if (text.StartsWith (Environment.NewLine))
                    {
                        text = text.Substring (Environment.NewLine.Length);
                    }
                }
                else
                { }
                lineIsWhitespace = lineIsWhitespace && text.Trim () == "";
                Output.Write (text);
                int newlines = CountLines (text);
                lastPrintedLine += newlines;
            }
            private int CountLines (string source)
            {
                int ret = 0;
                while (source != "")
                {
                    if (source.StartsWith (Environment.NewLine))
                    {
                        ret++;
                    }
                    source = source.Substring (1);
                }
                return ret;
            }
        }

        private string OutputExtension { get; } = ".cs";
        string Preprocess (string filename)
        {
            string source = File.ReadAllText (filename);
            ParseState state = new ParseState (source);
            state.Parse ();
            state.Output.Flush ();
            var outname = Path.ChangeExtension (filename, OutputExtension);
            StreamWriter preprocessed = new StreamWriter (outname);
            var stream = state.Output.BaseStream;
            stream.Seek (0, SeekOrigin.Begin);
            var reader = new StreamReader (stream);
            while (!reader.EndOfStream)
            {
                preprocessed.WriteLine (reader.ReadLine ());
            }
            preprocessed.Flush ();
            preprocessed.Close ();
            return outname;
        }
        static void Main (string[] args)
        {
            Program p = new Program ();
            Console.WriteLine ("CSX v1.0 - asklar@microsoft.com");
            IEnumerable<string> sources = args;
            foreach (var sourceFile in sources)
            {
                Console.Write (sourceFile);
                var outname = p.Preprocess (sourceFile);
                Console.WriteLine ($" -> {outname} ok.");
            }

        }
    }
}