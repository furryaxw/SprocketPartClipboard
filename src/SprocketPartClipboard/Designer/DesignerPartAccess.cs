using System;
using Il2CppSprocket.VehicleDesigner;
using Il2CppSprocket.VehicleDesigner.Access;
using Il2CppSprocket.VehicleDesigner.Operations;
using Il2CppSprocket.Vehicles;
using Il2CppSprocket.Vehicles.Selection;
using UnityEngine;

namespace SprocketPartClipboard.Designer
{
    // 载具设计器的接入点：设计器根、具体编辑器、当前选中部件，以及编辑器自己持有的操作控制器。
    //
    // 编辑器只在设计器场景存在，且场景切换后旧代理会指向已销毁的原生对象，所以本类型不做缓存，
    // 每次按键都重新解析一遍。
    public sealed class DesignerPartAccess
    {
        private DesignerPartAccess(VehicleDesignerCore core, VehicleEditor editor)
        {
            Core = core;
            Editor = editor;
        }

        public VehicleDesignerCore Core { get; }

        public VehicleEditor Editor { get; }

        public static bool TryOpen(out DesignerPartAccess? access)
        {
            access = null;

            VehicleDesignerCore core = UnityEngine.Object.FindObjectOfType<VehicleDesignerCore>();
            if (core == null || core.Pointer == IntPtr.Zero || !core.HasEditor)
                return false;

            VehicleEditor editor = core.Editor;
            if (editor == null || editor.Pointer == IntPtr.Zero)
                return false;

            access = new DesignerPartAccess(core, editor);
            return true;
        }

        // 选中项优先取"活动"部件（多选时玩家最后点中的那个），退化路径取选择列表的第一项。
        public VehicleComponent? SelectedPart()
        {
            IVehicleObjectPicker picker = Editor.Picker;
            if (picker != null && picker.Pointer != IntPtr.Zero && picker.HasActive)
                return Live(picker.Active) ? picker.Active : null;

            IReadOnlyVehicleObjectSelection selection = Editor.SelectionReader;
            if (selection == null || selection.Pointer == IntPtr.Zero || selection.Count == 0)
                return null;

            VehicleSelectionRegister? register =
                new Il2CppSystem.Object(selection.Pointer).TryCast<VehicleSelectionRegister>();
            if (register == null || register.Pointer == IntPtr.Zero)
                return null;

            // interop 的 IReadOnlyList<T> 只暴露索引器。
            Il2CppSystem.Collections.Generic.List<VehicleComponent>? items =
                register.Items.TryCast<Il2CppSystem.Collections.Generic.List<VehicleComponent>>();
            if (items == null || items.Count == 0)
                return null;

            VehicleComponent first = items[0];
            return Live(first) ? first : null;
        }

        public VehicleOperations? Operations()
        {
            VehicleOperations operations = Editor.operations;
            if (operations != null && operations.Pointer != IntPtr.Zero)
                return operations;

            IVehicleOperationCreator creator = Editor.VehicleOperations;
            if (creator == null || creator.Pointer == IntPtr.Zero)
                return null;

            return new Il2CppSystem.Object(creator.Pointer).TryCast<VehicleOperations>();
        }

        // 被 Unity 销毁但代理仍存在的对象会让后续调用抛原生异常，所以每次使用前都验一次。
        private static bool Live(VehicleComponent? component)
        {
            return component != null && component.Pointer != IntPtr.Zero && component.gameObject != null;
        }
    }
}
