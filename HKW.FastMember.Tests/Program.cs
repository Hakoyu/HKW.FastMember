﻿using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;

namespace HKW.FastMember.Tests;

public class Program
{
    public static void Main()
    {
        var summary = BenchmarkRunner.Run<FastMemberPerformance>(new Config());
        Console.WriteLine();
        // Display a summary to match the output of the original Performance test
        foreach (
            var report in summary.Reports.OrderBy(
                r => r.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo
            )
        )
        {
            Console.WriteLine(
                "{0}: {1:N2} ns",
                report.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo,
                report.ResultStatistics.Median
            );
        }
        Console.WriteLine();
    }
}

public class FastMemberPerformance
{
    public string Value { get; set; }

    private FastMemberPerformance obj;
    private dynamic dlr;
    private PropertyInfo prop;
    private PropertyDescriptor descriptor;

    private TypeAccessor accessor;
    private ObjectAccessor wrapped;

    private Type type;

    [GlobalSetup]
    public void Setup()
    {
        obj = new FastMemberPerformance();
        dlr = obj;
        prop = typeof(FastMemberPerformance).GetProperty("Value");
        descriptor = TypeDescriptor.GetProperties(obj)["Value"];

        // FastMember specific code
        accessor = FastMember.TypeAccessor.Create(typeof(FastMemberPerformance));
        wrapped = FastMember.ObjectAccessor.Create(obj);

        type = typeof(FastMemberPerformance);
    }

    [Benchmark(Description = "1. Static C#", Baseline = true)]
    public string StaticCSharp()
    {
        obj.Value = "abc";
        return obj.Value;
    }

    [Benchmark(Description = "2. Dynamic C#")]
    public string DynamicCSharp()
    {
        dlr.Value = "abc";
        return dlr.Value;
    }

    [Benchmark(Description = "3. PropertyInfo")]
    public string PropertyInfo()
    {
        prop.SetValue(obj, "abc", null);
        return (string)prop.GetValue(obj, null);
    }

    [Benchmark(Description = "4. PropertyDescriptor")]
    public string PropertyDescriptor()
    {
        descriptor.SetValue(obj, "abc");
        return (string)descriptor.GetValue(obj);
    }

    [Benchmark(Description = "5. TypeAccessor.Create")]
    public string TypeAccessor()
    {
        accessor[obj, "Value"] = "abc";
        return (string)accessor[obj, "Value"];
    }

    [Benchmark(Description = "6. ObjectAccessor.Create")]
    public string ObjectAccessor()
    {
        wrapped["Value"] = "abc";
        return (string)wrapped["Value"];
    }

    [Benchmark(Description = "7. c# new()")]
#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CA1822 // Mark members as static
    public FastMemberPerformance CSharpNew()
#pragma warning restore CA1822 // Mark members as static
#pragma warning restore IDE0079 // Remove unnecessary suppression
    {
        return new FastMemberPerformance();
    }

    [Benchmark(Description = "8. Activator.CreateInstance")]
    public object ActivatorCreateInstance()
    {
        return Activator.CreateInstance(type);
    }

    [Benchmark(Description = "9. TypeAccessor.CreateNew")]
    public object TypeAccessorCreateNew()
    {
        return accessor.CreateNew();
    }
}

// BenchmarkDotNet settings (you can use the defaults, but these are tailored for this benchmark)
public class Config : ManualConfig
{
    public Config()
    {
        AddJob(Job.Default.WithLaunchCount(1));
        AddColumn(StatisticColumn.Median, StatisticColumn.StdDev);
        AddExporter(CsvExporter.Default, MarkdownExporter.Default, MarkdownExporter.GitHub);
        AddLogger(new ConsoleLogger());
    }
}
