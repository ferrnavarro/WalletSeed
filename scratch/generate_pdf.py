import sys

def main():
    if len(sys.argv) < 2:
        print("Usage: python generate_pdf.py <output_file> [text]")
        sys.exit(1)
        
    output_path = sys.argv[1]
    text = sys.argv[2] if len(sys.argv) > 2 else "__STUB_BANK__"
    
    stream_content = f"BT\n/F1 12 Tf\n72 712 Td\n({text}) Tj\nET\n"
    
    header = b"%PDF-1.4\n"
    obj1 = b"1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"
    obj2 = b"2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n"
    obj3 = b"3 0 obj\n<< /Type /Page /Parent 2 0 R /Resources 4 0 R /MediaBox [0 0 612 792] /Contents 5 0 R >>\nendobj\n"
    obj4 = b"4 0 obj\n<< /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >>\nendobj\n"

    stream_data = f"stream\n{stream_content}endstream\n".encode('ascii')
    obj5 = f"5 0 obj\n<< /Length {len(stream_content)} >>\n".encode('ascii') + stream_data + b"endobj\n"

    offsets = []
    current = len(header)
    offsets.append(current) # obj 1
    current += len(obj1)
    offsets.append(current) # obj 2
    current += len(obj2)
    offsets.append(current) # obj 3
    current += len(obj3)
    offsets.append(current) # obj 4
    current += len(obj4)
    offsets.append(current) # obj 5
    current += len(obj5)

    xref_pos = current

    xref = b"xref\n0 6\n0000000000 65535 f \n"
    for offset in offsets:
        xref += f"{offset:010d} 00000 n \n".encode('ascii')

    trailer = f"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref_pos}\n%%EOF\n".encode('ascii')

    pdf_data = header + obj1 + obj2 + obj3 + obj4 + obj5 + xref + trailer

    with open(output_path, 'wb') as f:
        f.write(pdf_data)
    print(f"Generated PDF with text '{text}' at {output_path}")

if __name__ == "__main__":
    main()
