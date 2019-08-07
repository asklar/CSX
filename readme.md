# CSX Compiler

### Background
React JS allows embedding html markup in JS (JSX). 

### Goal
Can we offer something similar: _embed **XAML** markup inside of **C#**_


* I built a compiler frontend that translates the mix of C#+XAML markup into pure C#
* It’s type-safe and reflects on the types of properties/events/… to emit the right kind of code.
  * For example if you say `<TextBlock FontSize="24">` it knows that the 24 needs to be interpreted as an `int`, not a `string`.
* It allows repeater-type scenarios inside a container and allows naming objects so that they can later be referenced:
```xml
<StackPanel
    Id="taskList"
    Grid.Row="1"
    Grid.Column="0"
    Grid.ColumnSpan="2">
    {
        return person.Tasks.Select(todo => CreateToDoItem(todo));
    }
</StackPanel>
<Grid.PointerReleased Handler={
        completionRateTextBlock.Opacity = 1.0;
        taskList.Visibility = taskList.Visibility == Visibility.Collapsed ? 
                              Visibility.Visible : Visibility.Collapsed;
    }/>
```
 
I have a test .netcore3 app using WinForms-hosting-XAML and I’ve integrated the CSX compiler as an MSBuild target rule to convert the CSX into CS.

With this you can modify the CSX file, F5 to rebuild & launch 😊
<img src="/CSX-Demo.gif"/>


