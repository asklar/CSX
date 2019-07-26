// comment
/* another comment
that goes here
*/
string f()
{
    return "this is a string";
}

T g()
{
    return 
        // a "string" inside a comment isn't a string
        <textblock size="20" font="arial" />;
}

T h()
{
    return 
//<textblock size="{getfontsize()}" > this is some text </textblock>
<grid size="20,10">
    <!-- test 123 -->
    <textblock size="20" 
        font="arial" 
        grid.row.foo.bar="1" />
    <textblock text={f(123)} size={f("abc", "def")} />
    <textblock text="{f(123)}"/>
    <textblock text={   1234 + 4 }/>
</grid>
;
}

T k()
{
return
    <CheckBox
        Content={todo.Name}
        IsChecked={todo.IsCompleted}
        FontSize="18"
        VerticalContentAlignment="Center"
        Checked={(sender, args) => todo.IsCompleted = sender.IsChecked.GetValueOrDefault()}
        Unchecked={(sender, args) => todo.IsCompleted = sender.IsChecked.GetValueOrDefault()}/>;
}