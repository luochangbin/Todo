namespace TodoWidget.Core;

/// <summary>已完成事项的显示范围；未完成事项始终显示，不参与过滤。</summary>
public enum CompletedRange
{
    Week,
    Month,
    All,
}
