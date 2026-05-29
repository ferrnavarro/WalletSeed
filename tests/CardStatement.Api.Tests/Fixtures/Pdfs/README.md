# PDF Fixtures for Multi-Bank Integration Tests

These PDF files are minimal, valid text-based PDF files used for testing multi-bank detection and routing.

## Generation Method

They were generated using the python script in `scratch/generate_pdf.py`:

```bash
# To regenerate stub-marker.pdf:
python3 scratch/generate_pdf.py tests/CardStatement.Api.Tests/Fixtures/Pdfs/stub-marker.pdf "__STUB_BANK__"

# To regenerate neither.pdf:
python3 scratch/generate_pdf.py tests/CardStatement.Api.Tests/Fixtures/Pdfs/neither.pdf "SOME OTHER UNRELATED TEXT"
```
