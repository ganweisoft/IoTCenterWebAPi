﻿using System;

namespace IoTCenterCore.ExcelHelper
{
    [AttributeUsage(AttributeTargets.Property)]
    public class FieldAttribute : Attribute
    {
        public bool Ignore { get; set; }
        public string Name { get; set; }
    }
}
