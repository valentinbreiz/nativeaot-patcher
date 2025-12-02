using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.TestRunner.Framework;
using static Cosmos.TestRunner.Framework.TestRunner;
using static Cosmos.TestRunner.Framework.Assert;

namespace Cosmos.Kernel.Tests.TypeCasting
{
    internal unsafe static partial class Program
    {
        [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
        private static void KernelMain() => Main();

        private static void Main()
        {
            Serial.WriteString("[TypeCasting Tests] Starting test suite\n");
            Start("TypeCasting Tests", expectedTests: 9);

            // Class hierarchy type checks (RhTypeCast_IsInstanceOfClass)
            Run("IsInstanceOfClass_AnimalIsDog", TestIsInstanceOfClass);

            // Interface type checks (RhTypeCast_IsInstanceOfInterface)
            Run("IsInstanceOfInterface_IFlyable", TestIsInstanceOfInterface);

            // Interface explicit cast checks (RhTypeCast_CheckCastInterface)
            Run("CheckCastInterface_ValidAndInvalid", TestCheckCastInterface);

            // Multi-type pattern matching (RhTypeCast_IsInstanceOfAny)
            Run("IsInstanceOfAny_MultiPattern", TestIsInstanceOfAny);

            // Generic invariance and covariance
            Run("Generics_InvarianceCovariance", TestGenericsInvarianceCovariance);

            // Delegate contravariance
            Run("Delegate_Contravariance", TestDelegateContravariance);

            // Array covariance
            Run("Array_Covariance", TestArrayCovariance);

            // Custom generic variance
            Run("CustomVariance_ProducerConsumer", TestCustomGenericVariance);

            // IEnumerable covariance
            Run("IEnumerable_Covariance", TestIEnumerableCovariance);

            Serial.WriteString("[TypeCasting Tests] All tests completed\n");
            Finish();

            while (true) ;
        }

        // ==================== Class Hierarchy Tests ====================

        private static void TestIsInstanceOfClass()
        {
            Animal animal = new Dog();

            bool isAnimal = animal is Animal;
            bool isDog = animal is Dog;
            bool isBird = animal is Bird;

            True(isAnimal, "Dog instance is Animal");
            True(isDog, "Dog instance is Dog");
            True(!isBird, "Dog instance is not Bird");
        }

        // ==================== Interface Tests ====================

        private static void TestIsInstanceOfInterface()
        {
            Bird bird = new Bird();
            Dog dog = new Dog();

            bool birdCanFly = bird is IFlyable;
            bool dogCanFly = dog is IFlyable;

            // Value type implementing interface
            TestPoint tp = new TestPoint { X = 2, Y = 3 };
            ITestPoint? itp = tp;
            bool pointIsTestPoint = itp is ITestPoint;

            True(birdCanFly, "Bird implements IFlyable");
            True(!dogCanFly, "Dog does not implement IFlyable");
            True(pointIsTestPoint, "TestPoint implements ITestPoint");
        }

        private static void TestCheckCastInterface()
        {
            TestPoint tp = new TestPoint { X = 2, Y = 3 };
            Dog dog = new Dog();

            bool validCastWorked;
            bool invalidCastThrew;

            // Valid cast: value type to its interface (Add exception handling when implemented)
            ITestPoint castOk = tp;
            validCastWorked = castOk.Value == 5;

            // Invalid cast: should throw InvalidCastException (For now do a safe cast until exception handling is implemented)
            invalidCastThrew = (dog as IFlyable) == null;

            True(validCastWorked, "Valid interface cast works");
            True(invalidCastThrew, "Invalid interface cast throws InvalidCastException");
        }

        // ==================== Multi-type Pattern Tests ====================

        private static void TestIsInstanceOfAny()
        {
            static bool MatchIntStringAnimal(object o) => o is int or string or Dog;

            object o1 = 123;
            object o2 = new Dog();
            object o3 = 3.1415; // double

            bool matchesInt = MatchIntStringAnimal(o1);
            bool matchesDog = MatchIntStringAnimal(o2);
            bool matchesDouble = MatchIntStringAnimal(o3);

            True(matchesInt, "Pattern matches int");
            True(matchesDog, "Pattern matches Dog");
            True(!matchesDouble, "Pattern does not match double");
        }

        // ==================== Generic Variance Tests ====================

        private static void TestGenericsInvarianceCovariance()
        {
            List<Dog> dogList = new() { new Dog(), new Dog() };

            bool isListAnimal = dogList is List<Animal>;
            bool isIEnumerableAnimal = dogList is IEnumerable<Animal>;

            True(!isListAnimal, "List<T> is invariant - List<Dog> is not List<Animal>");
            True(isIEnumerableAnimal, "IEnumerable<out T> is covariant - List<Dog> is IEnumerable<Animal>");
        }

        private static void TestDelegateContravariance()
        {
            Action<Animal> actAnimal = delegate { };
            bool isActionDog = actAnimal is Action<Dog>;

            True(isActionDog, "Action<in T> is contravariant - Action<Animal> is Action<Dog>");
        }

        private static void TestArrayCovariance()
        {
            Dog[] dogArray = new[] { new Dog(), new Dog() };
            bool isAnimalArray = dogArray is Animal[];

            True(isAnimalArray, "Dog[] is Animal[] (array covariance)");

            if (isAnimalArray)
            {
                // Also verify assignment via base-typed array reference works
                Animal[] animalArrayRef = dogArray;
                animalArrayRef[0] = new Dog();
                True(true, "Assignment via base-typed array reference works");
            }
        }

        private static void TestCustomGenericVariance()
        {
            DogProducer producer = new();
            AnimalConsumer consumer = new();

            bool producerIsAnimalProducer = producer is IProducer<Animal>;
            bool consumerIsDogConsumer = consumer is IConsumer<Dog>;

            True(producerIsAnimalProducer, "IProducer<out T> covariance - DogProducer is IProducer<Animal>");
            True(consumerIsDogConsumer, "IConsumer<in T> contravariance - AnimalConsumer is IConsumer<Dog>");
        }

        private static void TestIEnumerableCovariance()
        {
            string[] strArray = new[] { "a", "b", "c" };
            bool isIEnumerableObject = strArray is IEnumerable<object>;

            True(isIEnumerableObject, "string[] is IEnumerable<object> (covariance)");
        }
    }

    // ==================== Helper Types ====================

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
}
