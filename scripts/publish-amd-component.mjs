// Fills ui/src-tauri/agent/amd: always the AMD component licence, plus NeuroTune.AmdAdlx.dll when the private
// AMD component is checked out in src/NeuroTune.AmdAdlx (official builds). Run from ui/ after agent publish.
import { execFileSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync } from 'node:fs';

const output = 'src-tauri/agent/amd';
const project = '../src/NeuroTune.AmdAdlx/NeuroTune.AmdAdlx.csproj';
mkdirSync(output, { recursive: true });
copyFileSync('../licenses/AMD-COMPONENT-EULA.txt', `${output}/AMD-COMPONENT-EULA.txt`);
if (existsSync(project)) execFileSync('dotnet', ['build', project, '-c', 'Release', '-o', output], { stdio: 'inherit' });
else console.log('AMD component not checked out: this build has no AMD Radeon settings.');
