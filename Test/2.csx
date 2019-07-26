using System;
using System.Linq;

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
    private SolidColorBrush rateToColor(float x)
    {
        // Windows.UI.Colors.PaleVioletRed
        return new SolidColorBrush(Windows.UI.Colors.PaleVioletRed);
    }

    public UIElement Do()
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
                FontSize="48"
                Text={person.Name}
                HorizontalAlignment="Left"
                Grid.Row="0"
                Grid.Column="0"/>
            <TextBlock
                Id="completionRateTextBlock"
                FontSize="72"
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