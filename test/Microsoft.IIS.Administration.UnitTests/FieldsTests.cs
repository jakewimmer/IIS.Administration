// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.UnitTests
{
    using Microsoft.IIS.Administration.Core.Utils;
    using System;
    using Xunit;

    public class FieldsTests
    {
        [Fact]
        public void IdField_IsAlwaysIncluded()
        {
            var fields = new Fields("name");

            Assert.True(fields.Exists("id"));
            Assert.True(fields.Exists("name"));
        }

        [Fact]
        public void Exists_IsCaseInsensitive()
        {
            var fields = new Fields("Name");

            Assert.True(fields.Exists("name"));
            Assert.True(fields.Exists("NAME"));
        }

        [Fact]
        public void Exists_MatchesParentOfNestedField()
        {
            var fields = new Fields("model.name");

            Assert.True(fields.Exists("model"));
            Assert.False(fields.Exists("other"));
        }

        [Fact]
        public void Wildcard_MatchesEveryField()
        {
            var fields = new Fields("*");

            Assert.True(fields.Exists("anything"));
            Assert.True(fields.Exists("nested.deeply"));
            Assert.True(fields.HasFields);
        }

        [Fact]
        public void NullFields_MatchesEverything()
        {
            var fields = new Fields((string[])null);

            Assert.True(fields.Exists("anything"));
        }

        [Fact]
        public void EmptyField_IsIgnored_AndDoesNotSetHasFields()
        {
            var fields = new Fields(string.Empty);

            Assert.False(fields.HasFields);
            Assert.True(fields.Exists("id"));
            Assert.False(fields.Exists("name"));
        }

        [Fact]
        public void Constructor_TrimsWhitespace()
        {
            var fields = new Fields("  name  ");

            Assert.True(fields.Exists("name"));
        }

        [Fact]
        public void Filter_ReturnsSubFieldsUnderPrefix()
        {
            var fields = new Fields("model.name", "model.age", "other");

            Fields filtered = fields.Filter("model");

            Assert.True(filtered.Exists("name"));
            Assert.True(filtered.Exists("age"));
            Assert.False(filtered.Exists("other"));
        }

        [Fact]
        public void Filter_OnAllFields_ReturnsEmpty()
        {
            var fields = new Fields((string[])null);

            Fields filtered = fields.Filter("model");

            Assert.False(filtered.Exists("name"));
            Assert.True(filtered.Exists("id"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Filter_WithNullOrEmpty_Throws(string filter)
        {
            var fields = new Fields("model.name");

            Assert.Throws<ArgumentNullException>(() => fields.Filter(filter));
        }
    }
}
