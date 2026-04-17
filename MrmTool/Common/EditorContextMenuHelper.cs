using System;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;
using WinUIEditor;

namespace MrmTool.Common
{
    internal static class EditorContextMenuHelper
    {
        public static void DisableBuiltInContextMenu(CodeEditorControl control)
        {
            if (control?.Editor is null)
                return;

            try
            {
                // Disable WinUIEditor built-in popup (English) menu
                control.Editor.UsePopUp((PopUp)0);
            }
            catch
            {
                // Best-effort: if API changes, we still keep app functional.
            }
        }

        public static MenuFlyout CreateChineseEditorMenu(Editor editor)
        {
            static string L(string key) => LocalizationService.GetString(key);
            var menu = new MenuFlyout();

            // Reduce perceived latency: disable open/close animations.
            try
            {
                var style = new Style(typeof(MenuFlyoutPresenter));
                style.Setters.Add(new Setter(UIElement.TransitionsProperty, new TransitionCollection()));
                menu.MenuFlyoutPresenterStyle = style;
            }
            catch { }

            MenuFlyoutItem? undo = null;
            MenuFlyoutItem? redo = null;
            MenuFlyoutItem? cut = null;
            MenuFlyoutItem? copy = null;
            MenuFlyoutItem? paste = null;
            MenuFlyoutItem? del = null;
            MenuFlyoutItem? selectAll = null;

            MenuFlyoutItem Item(string text, Symbol symbol, Action action)
            {
                var it = new MenuFlyoutItem
                {
                    Text = text,
                    Icon = new SymbolIcon(symbol),
                };
                it.Click += (_, __) =>
                {
                    try { action(); } catch { }
                };
                return it;
            }

            undo = Item(L("Editor.Undo"), Symbol.Undo, () => editor.Undo());
            redo = Item(L("Editor.Redo"), Symbol.Redo, () => editor.Redo());
            cut = Item(L("Editor.Cut"), Symbol.Cut, () => editor.Cut());
            copy = Item(L("Editor.Copy"), Symbol.Copy, () => editor.Copy());
            paste = Item(L("Editor.Paste"), Symbol.Paste, () => editor.Paste());
            del = Item(L("Editor.Delete"), Symbol.Delete, () => editor.Clear());
            selectAll = Item(L("Editor.SelectAll"), Symbol.SelectAll, () => editor.SelectAll());

            menu.Items.Add(undo);
            menu.Items.Add(redo);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(cut);
            menu.Items.Add(copy);
            menu.Items.Add(paste);
            menu.Items.Add(del);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(selectAll);

            menu.Opening += (_, __) =>
            {
                try
                {
                    undo!.IsEnabled = editor.CanUndo();
                    redo!.IsEnabled = editor.CanRedo();

                    bool hasSelection = editor.SelectionEmpty is false;
                    bool isReadOnly = editor.ReadOnly;

                    cut!.IsEnabled = hasSelection && !isReadOnly;
                    copy!.IsEnabled = hasSelection;
                    paste!.IsEnabled = !isReadOnly && editor.CanPaste();
                    del!.IsEnabled = hasSelection && !isReadOnly;
                    selectAll!.IsEnabled = editor.TextLength > 0;
                }
                catch
                {
                    // ignore
                }
            };

            return menu;
        }

        public static void ShowAtPointer(MenuFlyout menu, UIElement target, RightTappedRoutedEventArgs e)
        {
            if (menu is null || target is null || e is null)
                return;

            var p = e.GetPosition(target);
            menu.ShowAt(target, p);
        }
    }
}

