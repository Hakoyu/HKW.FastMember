using HKW.FastMember;

namespace HKW.FastMemberTests;

[TestClass]
public sealed class ConcurrencyTests
{
    private const int WorkerCount = 16;
    private const int IterationCount = 250;

    [TestMethod]
    public async Task SameTypeAccessors_AreCreatedOnceAndUsedConcurrently()
    {
        using var barrier = new Barrier(WorkerCount);
        var tasks = Enumerable.Range(0, WorkerCount).Select(worker => Task.Run(() =>
        {
            var model = new SameTypeModel();
            barrier.SignalAndWait();
            TypeAccessor? firstAccessor = null;

            for (var iteration = 0; iteration < IterationCount; iteration++)
            {
                var accessor = TypeAccessor.Create(typeof(SameTypeModel));
                firstAccessor ??= accessor;
                Assert.AreSame(firstAccessor, accessor);

                var expected = worker * IterationCount + iteration;
                accessor[model, nameof(SameTypeModel.Value)] = expected;
                Assert.AreEqual(expected, accessor[model, nameof(SameTypeModel.Value)]);
            }

            return firstAccessor;
        })).ToArray();

        var accessors = await Task.WhenAll(tasks);

        Assert.IsNotNull(accessors[0]);
        foreach (var accessor in accessors)
            Assert.AreSame(accessors[0], accessor);
    }

    [TestMethod]
    public async Task MultipleTypeAccessors_AreGeneratedAndUsedConcurrently()
    {
        var scenarios = new (Type Type, object Target, string Member, object Value)[]
        {
            (typeof(FirstConcurrentModel), new FirstConcurrentModel(), nameof(FirstConcurrentModel.Number), 11),
            (typeof(SecondConcurrentModel), new SecondConcurrentModel(), nameof(SecondConcurrentModel.Text), "second"),
            (typeof(ThirdConcurrentModel), new ThirdConcurrentModel(), nameof(ThirdConcurrentModel.Enabled), true),
            (typeof(FourthConcurrentModel), new FourthConcurrentModel(), nameof(FourthConcurrentModel.Total), 44L),
        };
        using var barrier = new Barrier(scenarios.Length);

        var tasks = scenarios.Select(scenario => Task.Run(() =>
        {
            barrier.SignalAndWait();
            var accessor = TypeAccessor.Create(scenario.Type);

            for (var iteration = 0; iteration < IterationCount; iteration++)
            {
                accessor[scenario.Target, scenario.Member] = scenario.Value;
                Assert.AreEqual(scenario.Value, accessor[scenario.Target, scenario.Member]);
                Assert.AreSame(accessor, TypeAccessor.Create(scenario.Type));
            }
        })).ToArray();

        await Task.WhenAll(tasks);
    }

    [TestMethod]
    public async Task ObjectAccessors_KeepSourceStateIsolatedAcrossWorkers()
    {
        var models = Enumerable.Range(0, WorkerCount).Select(_ => new IsolationModel()).ToArray();
        var accessors = models.Select(ObjectAccessor.Create).ToArray();
        using var barrier = new Barrier(WorkerCount);

        var tasks = Enumerable.Range(0, WorkerCount).Select(worker => Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (var iteration = 0; iteration < IterationCount; iteration++)
            {
                var expected = $"{worker}:{iteration}";
                accessors[worker][nameof(IsolationModel.Value)] = expected;
                Assert.AreEqual(expected, accessors[worker][nameof(IsolationModel.Value)]);
                Assert.AreSame(models[worker], accessors[worker].Source);
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        for (var worker = 0; worker < WorkerCount; worker++)
            Assert.AreEqual($"{worker}:{IterationCount - 1}", models[worker].Value);
    }

    [TestMethod]
    public async Task ParallelReadsAndWrites_DoNotRaceWithAccessorCacheUse()
    {
        var models = Enumerable.Range(0, WorkerCount).Select(_ => new ReadWriteModel()).ToArray();
        using var barrier = new Barrier(WorkerCount);

        var tasks = Enumerable.Range(0, WorkerCount).Select(worker => Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (var iteration = 0; iteration < IterationCount; iteration++)
            {
                var accessor = TypeAccessor.Create(typeof(ReadWriteModel));
                var expectedNumber = worker * IterationCount + iteration;
                var expectedText = expectedNumber.ToString();

                Assert.IsTrue(accessor.TrySetValue(models[worker], nameof(ReadWriteModel.Number), expectedNumber));
                Assert.IsTrue(accessor.TrySetValue(models[worker], nameof(ReadWriteModel.Text), expectedText));
                Assert.IsTrue(accessor.TryGetValue(models[worker], nameof(ReadWriteModel.Number), out var number));
                Assert.IsTrue(accessor.TryGetValue(models[worker], nameof(ReadWriteModel.Text), out var text));
                Assert.AreEqual(expectedNumber, number);
                Assert.AreEqual(expectedText, text);
            }
        })).ToArray();

        await Task.WhenAll(tasks);
    }

    private sealed class SameTypeModel
    {
        public int Value { get; set; }
    }

    private sealed class FirstConcurrentModel
    {
        public int Number { get; set; }
    }

    private sealed class SecondConcurrentModel
    {
        public string? Text { get; set; }
    }

    private sealed class ThirdConcurrentModel
    {
        public bool Enabled { get; set; }
    }

    private sealed class FourthConcurrentModel
    {
        public long Total { get; set; }
    }

    private sealed class IsolationModel
    {
        public string? Value { get; set; }
    }

    private sealed class ReadWriteModel
    {
        public int Number { get; set; }
        public string? Text { get; set; }
    }
}
