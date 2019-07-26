using System;
using System.Linq;
using System.Security.Principal;

class Person
{
    public float CompletionRate = 0.87f;
    public string Name = "Jean Dough";
    public List<string> Tasks = new List<string>() { "buy apples", "call mom", "???", "profit" };
}

class Demo
{
    private UIElement CreateToDoItem(string s)
    {
        return <TextBlock Text={ $"This is a todo item: {s}"} />;
    }
    private SolidColorBrush rateToColor(int x)
    {
        return new SolidColorBrush(Windows.UI.Colors.PaleVioletRed);
    }

    public static void Main(string[] args)
    {
        Console.WriteLine("Hello from demo");
        Windows.UI.Xaml.Hosting.WindowsXamlManager.InitializeForCurrentThread();
        Windows.UI.Xaml.Controls.ColumnDefinition c = new Windows.UI.Xaml.Controls.ColumnDefinition();
        c.Width = new Windows.UI.Xaml.GridLength(1, GridUnitType.Auto);
        new Demo().f();
    }

    public void f()
    {
        Person person = new Person();
        var g = <Grid
            Background={rateToColor(person.CompletionRate)}>
            <Grid.RowDefinitions>
                <RowDefinition/>
                <RowDefinition/>
            </Grid.RowDefinitions>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <TextBlock
                FontSize="24"
                Text={person.Name}
                HorizontalAlignment="Left"
                Grid.Row="0"
                Grid.Column="0"/>
            <TextBlock
                Id="completionRateTextBlock"
                FontSize="24"
                Text={person.CompletionRate.ToString("P0")}
                HorizontalAlignment="Right"
                Grid.Row="0"
                Grid.Column="1"/>
            <StackPanel
                Id="taskList"
                Grid.Row="1"
                Grid.Column="0"
                Grid.ColumnSpan="2">
                {
                    return person.Tasks.Select(todo => CreateToDoItem(todo));
                }
                </StackPanel>
            <Grid.PointerPressed Handler={
                    completionRateTextBlock.Opacity = 0.5;
                }/>
            <Grid.PointerReleased Handler={
                    completionRateTextBlock.Opacity = 1.0;
                    taskList.Visibility = taskList.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
                }/>
        </Grid>;

        return g;
    }
}