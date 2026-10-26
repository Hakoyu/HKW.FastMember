namespace HKW.FastMember.Generater.Tests;

internal class Program
{
    private static void Main()
    {
        var foo = new Foo();
        var acc = ObjectAccessor.Create<Foo>(foo);
        acc["Name"] = "";
    }
}

internal class Foo
{
    public int Number { get; set; } = 114514;

    public string Name { get; set; } = "1919810";
}
