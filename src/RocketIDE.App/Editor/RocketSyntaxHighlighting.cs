using System.IO;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace RocketIDE.App.Editor;

public static class RocketSyntaxHighlighting
{
    private static readonly Lazy<IHighlightingDefinition> LazyDefinition = new(CreateDefinition);

    public static IHighlightingDefinition Definition => LazyDefinition.Value;

    private static IHighlightingDefinition CreateDefinition()
    {
        const string xshd = """
            <?xml version="1.0"?>
            <SyntaxDefinition name="Rocket" extensions=".rocket" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
              <Property name="RocketIDE.SyntaxVersion" value="1" />
              <Color name="Comment" foreground="#6A9955" />
              <Color name="String" foreground="#CE9178" />
              <Color name="Number" foreground="#B5CEA8" />
              <Color name="Keyword" foreground="#C586C0" fontWeight="bold" />
              <Color name="Declaration" foreground="#569CD6" fontWeight="bold" />
              <Color name="Type" foreground="#4EC9B0" />
              <Color name="Constant" foreground="#4FC1FF" />
              <Color name="Operator" foreground="#D4D4D4" />
              <Color name="Punctuation" foreground="#D4D4D4" />
              <RuleSet>
                <Rule color="Comment">[#].*$</Rule>
                <Span color="String">
                  <Begin>&quot;</Begin>
                  <End>&quot;</End>
                  <RuleSet>
                    <Span begin="\\" end="." />
                  </RuleSet>
                </Span>
                <Span color="String">
                  <Begin>'</Begin>
                  <End>'</End>
                  <RuleSet>
                    <Span begin="\\" end="." />
                  </RuleSet>
                </Span>
                <Rule color="Number">\b(?:[0-9]+\.[0-9]+|[0-9]+)\b</Rule>
                <Keywords color="Declaration">
                  <Word>fn</Word><Word>struct</Word><Word>enum</Word><Word>impl</Word><Word>import</Word>
                </Keywords>
                <Keywords color="Keyword">
                  <Word>if</Word><Word>else</Word><Word>while</Word><Word>for</Word><Word>in</Word>
                  <Word>break</Word><Word>continue</Word><Word>return</Word><Word>match</Word><Word>case</Word>
                  <Word>pub</Word><Word>let</Word><Word>var</Word><Word>and</Word><Word>or</Word><Word>not</Word>
                </Keywords>
                <Keywords color="Type">
                  <Word>Int</Word><Word>Float</Word><Word>Bool</Word><Word>Char</Word><Word>String</Word>
                  <Word>Unit</Word><Word>Array</Word><Word>Slice</Word><Word>Option</Word><Word>Result</Word>
                </Keywords>
                <Keywords color="Constant"><Word>true</Word><Word>false</Word></Keywords>
                <Rule color="Type">\b[A-Z][A-Za-z0-9_]*\b</Rule>
                <Rule color="Operator">-&gt;|\.\.|==|!=|&lt;=|&gt;=|[+\-*/=&lt;&gt;?]</Rule>
                <Rule color="Punctuation">[()\[\]:,.]</Rule>
              </RuleSet>
            </SyntaxDefinition>
            """;

        using var stringReader = new StringReader(xshd);
        using var reader = XmlReader.Create(stringReader);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
