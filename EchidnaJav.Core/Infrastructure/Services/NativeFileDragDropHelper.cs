using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav.Core.Infrastructure.Services
{
    public static class NativeFileDragDropHelper
    {
        public static event Action<string>? OnFileDropped;

        public static void TriggerFileDrop(string filePath)
        {
            OnFileDropped?.Invoke(filePath);
        }
    }
}
