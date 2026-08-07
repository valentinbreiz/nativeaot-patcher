using System;
using System.Collections.Generic;
using Cosmos.TestingFramework;
using Cosmos.TestingFramework.Attributes;

namespace Cosmos.Kernel.Tests.TypeCasting;

#pragma warning disable CA2201 // Do not raise reserved exception types

[TestClass]
public class Tests
{
    [TestMethod]
    public static void TestIsInstanceOfClass_AnimalIsDog()
    {
        Animal animal = new Dog();

#pragma warning disable IDE0150
        bool isAnimal = animal is Animal;
#pragma warning restore IDE0150
        bool isDog = animal is Dog;
        bool isBird = animal is Bird;

        Assert.True(isAnimal, "Dog instance is Animal");
        Assert.True(isDog, "Dog instance is Dog");
        Assert.True(!isBird, "Dog instance is not Bird");
    }

    [TestMethod]
    public static void TestIsInstanceOfInterface_IFlyable()
    {
        Bird bird = new Bird();
        Dog dog = new Dog();

        bool birdCanFly = bird is IFlyable;
        bool dogCanFly = dog is IFlyable;

        TestPoint tp = new TestPoint { X = 2, Y = 3 };
        ITestPoint? itp = tp;

#pragma warning disable IDE0150
        bool pointIsTestPoint = itp is ITestPoint;
#pragma warning restore IDE0150

        Assert.True(birdCanFly, "Bird implements IFlyable");
        Assert.True(!dogCanFly, "Dog does not implement IFlyable");
        Assert.True(pointIsTestPoint, "TestPoint implements ITestPoint");
    }

    [TestMethod]
    public static void TestCheckCastInterface_ValidAndInvalid()
    {
        TestPoint tp = new TestPoint { X = 2, Y = 3 };
        Dog dog = new Dog();

        bool validCastWorked;
        bool invalidCastThrew;

        ITestPoint castOk = tp;
        validCastWorked = castOk.Value == 5;

        invalidCastThrew = (dog as IFlyable) == null;

        Assert.True(validCastWorked, "Valid interface cast works");
        Assert.True(invalidCastThrew, "Invalid interface cast throws InvalidCastException");
    }

    [TestMethod]
    public static void TestIsInstanceOfAny_MultiPattern()
    {
        static bool MatchIntStringAnimal(object o) => o is int or string or Dog;

        object o1 = 123;
        object o2 = new Dog();
        object o3 = 3.1415;

        bool matchesInt = MatchIntStringAnimal(o1);
        bool matchesDog = MatchIntStringAnimal(o2);
        bool matchesDouble = MatchIntStringAnimal(o3);

        Assert.True(matchesInt, "Pattern matches int");
        Assert.True(matchesDog, "Pattern matches Dog");
        Assert.True(!matchesDouble, "Pattern does not match double");
    }

    [TestMethod]
    public static void TestGenerics_InvarianceCovariance()
    {
        List<Dog> dogList = new() { new Dog(), new Dog() };

#pragma warning disable CS0184 // 'is' expression's given expression is never of the provided type
        bool isListAnimal = dogList is List<Animal>;
#pragma warning restore CS0184 // 'is' expression's given expression is never of the provided type
        bool isIEnumerableAnimal = dogList is IEnumerable<Animal>;

        Assert.True(!isListAnimal, "List<T> is invariant - List<Dog> is not List<Animal>");
        Assert.True(isIEnumerableAnimal, "IEnumerable<out T> is covariant - List<Dog> is IEnumerable<Animal>");
    }

    [TestMethod]
    public static void TestDelegate_Contravariance()
    {
        Action<Animal> actAnimal = delegate { };
        bool isActionDog = actAnimal is Action<Dog>;

        Assert.True(isActionDog, "Action<in T> is contravariant - Action<Animal> is Action<Dog>");
    }

    [TestMethod]
    public static void TestArray_Covariance()
    {
        Dog[] dogArray = new[] { new Dog(), new Dog() };
        bool isAnimalArray = dogArray is Animal[];

        Assert.True(isAnimalArray, "Dog[] is Animal[] (array covariance)");

        if (isAnimalArray)
        {
            Animal[] animalArrayRef = dogArray;
            animalArrayRef[0] = new Dog();
            Assert.True(true, "Assignment via base-typed array reference works");
        }
    }

    [TestMethod]
    public static void TestCustomVariance_ProducerConsumer()
    {
        DogProducer producer = new();
        AnimalConsumer consumer = new();

        bool producerIsAnimalProducer = producer is IProducer<Animal>;
        bool consumerIsDogConsumer = consumer is IConsumer<Dog>;

        Assert.True(producerIsAnimalProducer, "IProducer<out T> covariance - DogProducer is IProducer<Animal>");
        Assert.True(consumerIsDogConsumer, "IConsumer<in T> contravariance - AnimalConsumer is IConsumer<Dog>");
    }

    [TestMethod]
    public static void TestIEnumerable_Covariance()
    {
        string[] strArray = new[] { "a", "b", "c" };
        bool isIEnumerableObject = strArray is IEnumerable<object>;

        Assert.True(isIEnumerableObject, "string[] is IEnumerable<object> (covariance)");
    }

    [TestMethod]
    public static void TestTryCatch_Basic()
    {
        bool caughtException = false;
        try
        {
            throw new InvalidOperationException("Test exception");
        }
        catch (InvalidOperationException)
        {
            caughtException = true;
        }

        Assert.True(caughtException, "Exception should have been caught");
    }

    [TestMethod]
    public static void TestTryCatch_BaseType()
    {
        bool caughtException = false;
        try
        {
            throw new InvalidOperationException("Test");
        }
        catch (Exception)
        {
            caughtException = true;
        }

        Assert.True(caughtException, "Base Exception type should catch derived exceptions");
    }

    [TestMethod]
    public static void TestTryCatch_Message()
    {
        string? caughtMessage = null;
        try
        {
            throw new InvalidOperationException("Expected message");
        }
        catch (InvalidOperationException ex)
        {
            caughtMessage = ex.Message;
        }

        Assert.Equal("Expected message", caughtMessage);
    }

    [TestMethod]
    public static void TestTryCatch_Filter_When()
    {
        bool caughtWithFilter = false;
        string? caughtMessage = null;
        try
        {
            throw new InvalidOperationException("FilterMatch");
        }
        catch (InvalidOperationException ex) when (ex.Message == "FilterMatch")
        {
            caughtWithFilter = true;
            caughtMessage = ex.Message;
        }

        Assert.True(caughtWithFilter, "Exception filter 'when' should match and catch");
        Assert.Equal("FilterMatch", caughtMessage);
    }

    [TestMethod]
    public static void TestTryCatch_Filter_WhenFalse()
    {
        bool caughtSpecific = false;
        bool caughtGeneral = false;
        try
        {
            throw new InvalidOperationException("NoMatch");
        }
        catch (InvalidOperationException ex) when (ex.Message == "SomethingElse")
        {
            caughtSpecific = true;
        }
        catch (Exception)
        {
            caughtGeneral = true;
        }

        Assert.True(!caughtSpecific, "Exception filter 'when' should NOT match when condition is false");
        Assert.True(caughtGeneral, "General catch should handle exception when filter doesn't match");
    }

    [TestMethod]
    public static void TestTryFinally()
    {
        bool finallyExecuted = false;
        try
        {
        }
        finally
        {
            finallyExecuted = true;
        }

        Assert.True(finallyExecuted, "Finally block should always execute");
    }

    [TestMethod]
    public static void TestFilterAndCatchResume()
    {
        bool filterRan = false;
        bool catchRan = false;
        bool resumed = false;

        try
        {
            throw new Exception("FilterTest");
        }
        catch (Exception) when (RunFilter(ref filterRan))
        {
            catchRan = true;
        }

        resumed = true;

        Assert.True(filterRan, "Filter should have run");
        Assert.True(catchRan, "Catch should have run");
        Assert.True(resumed, "Execution should resume after catch");
    }

    private static bool RunFilter(ref bool flag)
    {
        flag = true;
        return true;
    }

    [TestMethod]
    public static void TestTryCatch_ConsoleWriteLineExMessage()
    {
        const string expectedMessage = "hello world!";
        string? caughtMessage = null;
        bool consoleWriteLineReturned = false;
        bool resumedAfterCatch = false;

        try
        {
            throw new Exception(expectedMessage);
        }
        catch (Exception ex)
        {
            caughtMessage = ex.Message;
            Console.WriteLine(ex.Message);
            consoleWriteLineReturned = true;
        }

        resumedAfterCatch = true;

        Assert.Equal(expectedMessage, caughtMessage);
        Assert.True(consoleWriteLineReturned,
            "Console.WriteLine(ex.Message) inside a catch must return without page-faulting");
        Assert.True(resumedAfterCatch,
            "Execution must resume after the catch funclet exits");
    }
}

internal struct TestPoint : ITestPoint
{
    public int X;
    public int Y;
    public readonly int Value => X + Y;
}

internal interface ITestPoint
{
    int Value { get; }
}

internal class Animal
{
}

internal class Dog : Animal
{
}

internal interface IFlyable
{
    void Fly();
}

internal class Bird : Animal, IFlyable
{
    public void Fly()
    {
    }
}

internal interface IProducer<out T>
{
    T Produce();
}

internal interface IConsumer<in T>
{
    void Consume(T item);
}

internal class DogProducer : IProducer<Dog>
{
    public Dog Produce() => new Dog();
}

internal class AnimalConsumer : IConsumer<Animal>
{
    public void Consume(Animal item)
    {
    }
}
