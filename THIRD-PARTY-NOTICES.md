# Avisos de terceiros

## Scrapling

O motor heurístico C# do RpaBlockly foi desenvolvido com referência verificável no mecanismo de adaptive relocation do Scrapling `v0.4.14`, commit `5d213a2d4764002bfc4fed33c32fe09fa8b0bf7f`.

Copyright (c) 2024, Karim shoair

BSD 3-Clause License

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

O pacote Python não é distribuído nem carregado pelo runtime. O harness de desenvolvimento está em `tools/scrapling-reference`; as divergências intencionais estão no ADR-008.

## Ferramentas de conformidade dos schemas

Os checks TypeScript em `tools/schema-conformance` instalam dependências de
desenvolvimento fixadas no `package-lock.json`. Elas não são carregadas pelo
worker, editor ou runtime:

- Ajv `8.20.0`, copyright (c) 2015-2021 Evgeny Poberezkin — MIT;
- ajv-formats `3.0.1`, copyright (c) 2020 Evgeny Poberezkin — MIT;
- TypeScript `5.9.2`, Microsoft Corporation — Apache-2.0.

Os textos integrais das licenças acompanham os respectivos pacotes instalados
pelo npm. O SBOM gerado por `tools/Generate-Sbom.ps1` inventaria os pacotes
NuGet efetivamente restaurados para a solução.
