using System.Windows;
using System.Windows.Controls;
using SiNetSQL.MVVM;

namespace SiNetProjectManagerV2.WPFUserControl;

/// <summary>
/// Selects the correct DataTemplate for a designer node based on its NodeType.
/// </summary>
public class NodeTemplateSelector : DataTemplateSelector
{
    public DataTemplate? StageTemplate { get; set; }
    public DataTemplate? DecisionTemplate { get; set; }
    public DataTemplate? CircleTemplate { get; set; }
    public DataTemplate? BarTemplate { get; set; }
    public DataTemplate? SubWorkflowTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not DesignerNodeViewModel node)
            return base.SelectTemplate(item, container);

        return node.NodeType switch
        {
            "Stage" => StageTemplate,
            "Decision" => DecisionTemplate,
            "Start" or "End" => CircleTemplate,
            "Fork" or "Join" => BarTemplate,
            "SubWorkflow" => SubWorkflowTemplate,
            _ => StageTemplate
        };
    }
}
