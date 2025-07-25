﻿using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HKW.FastMember.Tests;

[TestClass]
public class AnonsTests
{
    [TestMethod]
    public void TestAnonTypeAccess()
    {
        var obj = new { A = 123, B = "def" };

        var accessor = ObjectAccessor.Create(obj);
        Assert.AreEqual(123, accessor["A"]);
        Assert.AreEqual("def", accessor["B"]);
    }

    [TestMethod]
    public void TestAnonCtor()
    {
        var obj = new { A = 123, B = "def" };

        var accessor = TypeAccessor.Create(obj.GetType());
        Assert.IsFalse(accessor.CreateNewSupported);
    }

    [TestMethod]
    public void TestPrivateTypeAccess()
    {
        var obj = new Private { A = 123, B = "def" };

        var accessor = ObjectAccessor.Create(obj);
        Assert.AreEqual(123, accessor["A"]);
        Assert.AreEqual("def", accessor["B"]);
    }

    [TestMethod]
    public void TestPrivateTypeCtor()
    {
        var accessor = TypeAccessor.Create(typeof(Private));
        Assert.IsTrue(accessor.CreateNewSupported);
        object obj = accessor.CreateNew();
        Assert.IsNotNull(obj);
        Assert.IsInstanceOfType<Private>(obj);
    }

    private sealed class Private
    {
        public int A { get; set; }
        public string? B { get; set; }
    }
}
