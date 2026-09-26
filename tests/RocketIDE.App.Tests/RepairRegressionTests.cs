using System.Collections.Specialized;
using RocketIDE.App.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RocketIDE.App.ViewModels;
using RocketIDE.Rocket.Compiler;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class RepairRegressionTests
{
    [TestMethod]
    public void OutputViewport_FollowsRunStdoutAndExitAfterBuildHistory()
    {
        RunSta(() =>
        {
            var list = new OutputListBox();
            list.Width = 500; list.Height = 90;
            list.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListBox"><ScrollViewer CanContentScroll="True"><ItemsPresenter /></ScrollViewer></ControlTemplate>""");
            var output = new OutputViewModel();
            list.ItemsSource = output.Lines;
            Layout(list);
            output.AppendMany(Enumerable.Range(0, 25).Select(i => $"old build line {i}"));
            Pump(); Layout(list);
            output.AppendMany(["=== Rocket Run: fixture ===", "audit fixture", "stderr probe", "Rocket Run exited with code 0."]);
            Pump(); Layout(list);
            var scroll = FindScroll(list)!;
            Assert.IsTrue(scroll.ScrollableHeight > 0);
            Assert.AreEqual(scroll.ScrollableHeight, scroll.VerticalOffset, 0.1,
                "Run stdout and final exit must be in the visible tail, not hidden below old Build entries.");
            CollectionAssert.Contains(output.Lines.ToArray(), "audit fixture");
            Assert.AreEqual(29, output.Lines.Count, "Following output must preserve earlier history.");
            list.ItemsSource = null;
        });
    }

    [TestMethod]
    public void Tests_CompilerFailureWithoutSummaryTerminatesAndRerunClearsIt()
    {
        var vm = new TestsViewModel(); vm.BeginRun();
        vm.Apply(new RocketMessage("diagnostic", Level: "error", Code: "R5001", Message: "package test directory does not exist"));
        Complete(vm, 2, false, null);
        StringAssert.Contains(vm.SummaryText, "2");
        StringAssert.Contains(vm.SummaryText, "R5001");
        StringAssert.Contains(vm.SummaryText, "package test directory does not exist");
        Assert.IsFalse(vm.SummaryText.Contains("started"));
        vm.BeginRun(); Assert.AreEqual(0, vm.Items.Count);
        Complete(vm, 0, false, null);
        StringAssert.Contains(vm.SummaryText, "0");
        StringAssert.Contains(vm.SummaryText, "summary");
        Assert.IsFalse(vm.SummaryText.Contains("R5001"));
    }

    [TestMethod]
    public void Tests_CancellationAndLaunchFailureFinishIncompleteRowsTruthfully()
    {
        var vm = new TestsViewModel(); vm.BeginRun();
        vm.Apply(new RocketMessage("test-started", Name: "alpha"));
        Complete(vm, null, true, null);
        StringAssert.Contains(vm.SummaryText.ToLowerInvariant(), "cancel");
        Assert.AreEqual("CANCELLED", vm.Items[0].Status);
        vm.BeginRun(); Complete(vm, null, false, "Could not start compiler");
        StringAssert.Contains(vm.SummaryText, "Could not start compiler");
        Assert.IsFalse(vm.SummaryText.Contains("code 0"));
    }

    [TestMethod]
    public void Tests_SummaryRemainsVisibleButDoesNotHideNonzeroExit()
    {
        var vm = new TestsViewModel(); vm.BeginRun();
        vm.Apply(new RocketMessage("test-summary", Passed: 1, Failed: 0, Selected: 1));
        Complete(vm, 2, false, null);
        StringAssert.Contains(vm.SummaryText, "1 passed");
        StringAssert.Contains(vm.SummaryText, "2");
    }

    private static void Complete(TestsViewModel vm, int? exit, bool cancelled, string? error)
    {
        vm.CompleteRun(exit, cancelled, error);
    }
    private static void Layout(FrameworkElement element)
    { element.Measure(new Size(500,90)); element.Arrange(new Rect(0,0,500,90)); element.UpdateLayout(); }
    private static ScrollViewer? FindScroll(DependencyObject node)
    {
        if(node is ScrollViewer scroll) return scroll;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
            if(FindScroll(VisualTreeHelper.GetChild(node,i)) is { } child) return child;
        return null;
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue=false));
        Dispatcher.PushFrame(frame);
    }
    private static void RunSta(Action action)
    {
        Exception? error=null;
        var thread=new Thread(()=>{try {action();} catch(Exception e){error=e;}});
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)));
        if(error is not null) throw new AssertFailedException(error.ToString());
    }
}
