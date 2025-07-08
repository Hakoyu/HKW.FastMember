using System;

namespace HKW.FastMember.Tests;

[TestClass]
public class ByRefPropTests
{
    [TestMethod]
    public void CanGetByRef()
    {
        var foo = new Foo { Val = 42 };

        var acc = ObjectAccessor.Create(foo);
        Assert.AreEqual(42, (int)acc["Val"]);
        Assert.AreEqual(42, (int)acc["Ref"]);
        Assert.AreEqual(42, (int)acc["RefReadOnly"]);
    }

    [TestMethod]
    public void CanSetByRef()
    {
        var foo = new Foo { Val = 42 };
        var acc = ObjectAccessor.Create(foo);
        acc["Val"] = 43;
        Assert.AreEqual(43, foo.Val);

        acc["Ref"] = 44;
        Assert.AreEqual(44, foo.Val);

        var ex = Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
        {
            acc["RefReadOnly"] = 45;
        });
        Assert.AreEqual("name", ex.ParamName);
        Assert.AreEqual(44, foo.Val);
    }

    public class Foo
    {
        private int _val;
        public int Val
        {
            get => _val;
            set => _val = value;
        }
        public ref int Ref => ref _val;
        public ref readonly int RefReadOnly => ref _val;
    }
}
