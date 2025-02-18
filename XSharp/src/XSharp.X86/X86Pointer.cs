using System.Text;
using XSharp.Base.ControlFlow;
using XSharp.X86.Registers;

namespace XSharp.X86;

public class X86Pointer
{

    public LabelObject? BaseLabel;

    public X86Variable? BaseVariable;

    public X86Register? BaseRegister;
    public X86Register? OffsetRegister;
    public X86Register? ModifierRegister;


    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append('[');

        if (BaseRegister != null)
        {
            sb.Append(BaseRegister);
        }

        if (BaseLabel != null)
        {
            sb.Append(BaseLabel);
        }

        if (BaseVariable != null)
        {
            sb.Append(BaseVariable);
        }


        if (OffsetRegister != null)
        {
            sb.Append('+');
            sb.Append(OffsetRegister);
        }

        if (ModifierRegister != null)
        {
            sb.Append('*');
            sb.Append(ModifierRegister);
        }

        sb.Append(']');

        return sb.ToString();
    }
}


public enum X86DataSize
{
    Byte,
    Word,
    DWord,
    QWord,
}
