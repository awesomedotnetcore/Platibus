﻿// The MIT License (MIT)
// 
// Copyright (c) 2017 Jesse Sweetland
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

namespace Platibus.SQL.Commands
{
    /// <summary>
    /// Abstract base class that supplies default command builders suitable for SQL Server, SQLite,
    /// and other databases with support for Transact-SQL query syntax.
    /// </summary>
    public abstract class CommonSubscriptionTrackingCommandBuilders : ISubscriptionTrackingCommandBuilders
    {
        /// <inheritdoc />
        public abstract CreateSubscriptionTrackingObjectsCommandBuilder NewCreateObjectsCommandBuilder();

        /// <inheritdoc />
        public virtual InsertSubscriptionCommandBuilder NewInsertSubscriptionCommandBuilder()
        {
            return new InsertSubscriptionCommandBuilder();
        }

        /// <inheritdoc />
        public virtual UpdateSubscriptionCommandBuilder NewUpdateSubscriptionCommandBuilder()
        {
            return new UpdateSubscriptionCommandBuilder();
        }

        /// <inheritdoc />
        public SelectSubscriptionsCommandBuilder NewSelectSubscriptionsCommandBuilder()
        {
            return new SelectSubscriptionsCommandBuilder();
        }

        /// <inheritdoc />
        public virtual DeleteSubscriptionCommandBuilder NewDeleteSubscriptionCommandBuilder()
        {
            return new DeleteSubscriptionCommandBuilder();
        }
    }
}
