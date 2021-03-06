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

using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;

namespace Platibus.MongoDB
{
    internal class QueuedMessageDocument
    {
        [BsonId]
        public ObjectId Id { get; set; }

        [BsonElement("queue")]
        public string Queue { get; set; }

        [BsonElement("messageId")]
        public string MessageId { get; set; }

        [BsonElement("headers")]
        [BsonDictionaryOptions(Representation = DictionaryRepresentation.Document)]
        public IDictionary<string, string> Headers { get; set; }

        [BsonElement("content")]
        public string Content { get; set; }

        [BsonElement("attempts")]
        public int Attempts { get; set; }

        [BsonElement("state")]
        public QueuedMessageState State { get; set; }

        [BsonElement("acknowledged")]
        public DateTime? Acknowledged { get; set; }

        [BsonElement("abandoned")]
        public DateTime? Abandoned { get; set; }
    }
}
