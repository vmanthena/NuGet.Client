// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using FluentAssertions;
using Xunit;

namespace NuGet.Test
{
    public class VerifyTests
    {
        [Fact]
        public void ArgumentIsNotNull_Throws_for_null_argument()
        {
            Assert.Throws<ArgumentNullException>(() => Verify.ArgumentIsNotNull((object)null, "param"));
        }

        [Fact]
        public void ArgumentIsNotNull_Exception_has_correct_parameter_name()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => Verify.ArgumentIsNotNull((object)null, "param"));
            exception.ParamName.Should().Be("param");
        }

        [Fact]
        public void ArgumentIsNotNull_Does_not_throw_for_non_null_argument()
        {
            Verify.ArgumentIsNotNull(new object(), "param");
        }
    }
}
