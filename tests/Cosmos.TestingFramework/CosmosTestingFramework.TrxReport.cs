using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Cosmos.TestingFramework.Attributes;
using Microsoft.Testing.Extensions.TrxReport.Abstractions;
using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Requests;

namespace Cosmos.TestingFramework
{
    internal partial class CosmosTestingFramework
    {
        private static void FillTrxProperties(TestNode testNode, MethodInfo test, Exception? ex = null)
        {
            testNode.Properties.Add(new TrxTestDefinitionName(testNode.Uid));
            testNode.Properties.Add(new TrxFullyQualifiedTypeNameProperty(test.DeclaringType!.FullName!));

            if (ex is not null)
            {
                testNode.Properties.Add(new TrxExceptionProperty(ex.Message, ex.StackTrace));
            }
        }
    }
}
