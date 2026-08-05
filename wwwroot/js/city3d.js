(() => {
    "use strict";

    const scenes = new Map();

    const vertexShaderSource = `
        attribute vec3 aPosition;
        attribute vec3 aNormal;
        uniform mat4 uProjection;
        uniform mat4 uView;
        uniform mat4 uModel;
        varying vec3 vNormal;
        void main() {
            vec3 normal = normalize(mat3(uModel) * aNormal);
            vNormal = normal;
            gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
        }
    `;

    const fragmentShaderSource = `
        precision mediump float;
        uniform vec3 uColor;
        uniform float uEmission;
        varying vec3 vNormal;
        void main() {
            vec3 normal = normalize(vNormal);
            vec3 lightDirection = normalize(vec3(-0.55, 1.0, 0.7));
            float diffuse = max(dot(normal, lightDirection), 0.0);
            float topLight = max(normal.y, 0.0) * 0.12;
            float edgeLight = (1.0 - abs(normal.y)) * 0.055;
            vec3 lit = uColor * (0.29 + diffuse * 0.66 + topLight + edgeLight);
            lit = mix(lit, uColor * 1.5, uEmission);
            gl_FragColor = vec4(lit, 1.0);
        }
    `;

    function perspective(out, fovy, aspect, near, far) {
        const f = 1 / Math.tan(fovy / 2);
        out.fill(0);
        out[0] = f / aspect;
        out[5] = f;
        out[10] = (far + near) / (near - far);
        out[11] = -1;
        out[14] = (2 * far * near) / (near - far);
        return out;
    }

    function orthographic(out, left, right, bottom, top, near, far) {
        out.fill(0);
        out[0] = 2 / (right - left);
        out[5] = 2 / (top - bottom);
        out[10] = -2 / (far - near);
        out[12] = -(right + left) / (right - left);
        out[13] = -(top + bottom) / (top - bottom);
        out[14] = -(far + near) / (far - near);
        out[15] = 1;
        return out;
    }

    function lookAt(out, eye, center) {
        let zx = eye[0] - center[0], zy = eye[1] - center[1], zz = eye[2] - center[2];
        let length = Math.hypot(zx, zy, zz) || 1;
        zx /= length; zy /= length; zz /= length;
        let xx = zz, xy = 0, xz = -zx;
        length = Math.hypot(xx, xz) || 1;
        xx /= length; xz /= length;
        const yx = zy * xz, yy = zz * xx - zx * xz, yz = -zy * xx;
        out[0] = xx; out[1] = yx; out[2] = zx; out[3] = 0;
        out[4] = xy; out[5] = yy; out[6] = zy; out[7] = 0;
        out[8] = xz; out[9] = yz; out[10] = zz; out[11] = 0;
        out[12] = -(xx * eye[0] + xy * eye[1] + xz * eye[2]);
        out[13] = -(yx * eye[0] + yy * eye[1] + yz * eye[2]);
        out[14] = -(zx * eye[0] + zy * eye[1] + zz * eye[2]);
        out[15] = 1;
        return out;
    }

    function multiply(out, a, b) {
        const result = new Float32Array(16);
        for (let column = 0; column < 4; column++) {
            for (let row = 0; row < 4; row++) {
                result[column * 4 + row] =
                    a[row] * b[column * 4] +
                    a[4 + row] * b[column * 4 + 1] +
                    a[8 + row] * b[column * 4 + 2] +
                    a[12 + row] * b[column * 4 + 3];
            }
        }
        out.set(result);
        return out;
    }

    function writeModel(out, x, y, z, width, height, depth, angle = 0) {
        const cosine = Math.cos(angle), sine = Math.sin(angle);
        out.fill(0);
        out[0] = cosine * width; out[2] = -sine * width;
        out[5] = height;
        out[8] = sine * depth; out[10] = cosine * depth;
        out[12] = x; out[13] = y; out[14] = z; out[15] = 1;
        return out;
    }

    function cubeVertices() {
        const positions = [], normals = [];
        const face = (a, b, c, d, normal) => {
            positions.push(...a, ...b, ...c, ...a, ...c, ...d);
            for (let index = 0; index < 6; index++) normals.push(...normal);
        };
        face([-.5,-.5,.5],[.5,-.5,.5],[.5,.5,.5],[-.5,.5,.5],[0,0,1]);
        face([.5,-.5,-.5],[-.5,-.5,-.5],[-.5,.5,-.5],[.5,.5,-.5],[0,0,-1]);
        face([.5,-.5,.5],[.5,-.5,-.5],[.5,.5,-.5],[.5,.5,.5],[1,0,0]);
        face([-.5,-.5,-.5],[-.5,-.5,.5],[-.5,.5,.5],[-.5,.5,-.5],[-1,0,0]);
        face([-.5,.5,.5],[.5,.5,.5],[.5,.5,-.5],[-.5,.5,-.5],[0,1,0]);
        face([-.5,-.5,-.5],[.5,-.5,-.5],[.5,-.5,.5],[-.5,-.5,.5],[0,-1,0]);
        return { positions: new Float32Array(positions), normals: new Float32Array(normals) };
    }

    function compile(gl, type, source) {
        const shader = gl.createShader(type);
        gl.shaderSource(shader, source);
        gl.compileShader(shader);
        if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
            const message = gl.getShaderInfoLog(shader);
            gl.deleteShader(shader);
            throw new Error(message || "Shader compilation failed");
        }
        return shader;
    }

    function createProgram(gl) {
        const program = gl.createProgram();
        const vertexShader = compile(gl, gl.VERTEX_SHADER, vertexShaderSource);
        const fragmentShader = compile(gl, gl.FRAGMENT_SHADER, fragmentShaderSource);
        gl.attachShader(program, vertexShader);
        gl.attachShader(program, fragmentShader);
        gl.linkProgram(program);
        gl.deleteShader(vertexShader);
        gl.deleteShader(fragmentShader);
        if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
            const message = gl.getProgramInfoLog(program);
            gl.deleteProgram(program);
            throw new Error(message || "Program linking failed");
        }
        return program;
    }

    function colorFromHex(value, fallback) {
        const source = typeof value === "string" && /^#[0-9a-f]{6}$/i.test(value) ? value : fallback;
        return [parseInt(source.slice(1,3),16)/255,parseInt(source.slice(3,5),16)/255,parseInt(source.slice(5,7),16)/255];
    }

    function scaleColor(color, factor) {
        return color.map(channel => Math.min(1,channel*factor));
    }

    function normalizeConfiguration(raw, count) {
        let value = raw;
        if (typeof value === "string") {
            try { value=JSON.parse(value); } catch { value={}; }
        }
        value=value||{};
        const read = (object, pascal, camel, fallback) => object?.[pascal] ?? object?.[camel] ?? fallback;
        const coreSource=read(value,"Core","core",{});
        const chipSources=read(value,"Chips","chips",[]);
        const routeSources=read(value,"Routes","routes",[]);
        const defaults=[[-7.8,-5.15],[7.1,-6.6],[-8,8.9],[10.4,5.25],[-12,1.2],[12,-1.1],[-3.8,-10.8],[4.4,10.9],[-12.5,-8.8],[12.7,8.7]];
        const defaultPosition=index => {
            if(defaults[index]) return defaults[index];
            const extra=index-defaults.length;
            const radius=14+Math.floor(extra/8)*4;
            const angle=-Math.PI/2+(extra%8)*Math.PI/4;
            return [Math.cos(angle)*radius,Math.sin(angle)*radius];
        };
        const chips=[];
        for(let index=0;index<count;index++) {
            const source=chipSources.find(chip => Number(read(chip,"Number","number",0))===index+1)||chipSources[index]||{};
            const fallback=defaultPosition(index);
            chips.push({
                number:index+1,
                x:Number(read(source,"X","x",fallback[0])),
                z:Number(read(source,"Z","z",fallback[1])),
                width:Number(read(source,"Width","width",3.18)),
                depth:Number(read(source,"Depth","depth",1.92)),
                color:colorFromHex(
                    read(source,"Color","color",index>=6?"#2F65A7":"#21492F"),
                    index>=6?"#2F65A7":"#21492F")
            });
        }
        const routes=chips.map((chip,index) => {
            const source=routeSources.find(route => Number(read(route,"ChipNumber","chipNumber",0))===chip.number)||{};
            const sourcePoints=read(source,"Points","points",[]);
            const points=sourcePoints.map(point => [Number(read(point,"X","x",0)),Number(read(point,"Z","z",0))]);
            return {
                points:points.length>=2?points:[[chip.x,chip.z],[chip.x*.28,chip.z*.28]],
                color:colorFromHex(
                    read(source,"Color","color",chip.number>=7?"#6CA8FF":"#B7ED63"),
                    chip.number>=7?"#6CA8FF":"#B7ED63")
            };
        });
        return {
            cameraSize:Number(read(value,"CameraSize","cameraSize",10.4)),
            groundColor:colorFromHex(read(value,"GroundColor","groundColor","#07110B"),"#07110B"),
            gridColor:colorFromHex(read(value,"GridColor","gridColor","#294A32"),"#294A32"),
            core:{
                x:Number(read(coreSource,"X","x",0)), z:Number(read(coreSource,"Z","z",0)),
                width:Number(read(coreSource,"Width","width",5.35)), depth:Number(read(coreSource,"Depth","depth",3.88)),
                color:colorFromHex(read(coreSource,"Color","color","#245235"),"#245235")
            },
            chips,
            routes
        };
    }

    function createScene(canvas, rawConfiguration, editorMode, dotNetReference) {
        const root = canvas.parentElement;
        const gl = canvas.getContext("webgl", {
            alpha: false,
            antialias: true,
            depth: true,
            powerPreference: "high-performance",
            preserveDrawingBuffer: false
        });
        if (!gl) {
            root.classList.add("webgl-unavailable");
            return null;
        }

        const program = createProgram(gl);
        const cube = cubeVertices();
        const positionBuffer = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, positionBuffer);
        gl.bufferData(gl.ARRAY_BUFFER, cube.positions, gl.STATIC_DRAW);
        const normalBuffer = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, normalBuffer);
        gl.bufferData(gl.ARRAY_BUFFER, cube.normals, gl.STATIC_DRAW);

        const attributes = {
            position: gl.getAttribLocation(program, "aPosition"),
            normal: gl.getAttribLocation(program, "aNormal")
        };
        const uniforms = {
            projection: gl.getUniformLocation(program, "uProjection"),
            view: gl.getUniformLocation(program, "uView"),
            model: gl.getUniformLocation(program, "uModel"),
            color: gl.getUniformLocation(program, "uColor"),
            emission: gl.getUniformLocation(program, "uEmission")
        };

        const projection = new Float32Array(16);
        const view = new Float32Array(16);
        const viewProjection = new Float32Array(16);
        const model = new Float32Array(16);
        let cssWidth = 1, cssHeight = 1, lastFrame = 0, frame = 0;
        let pointerX = 0, pointerY = 0, cameraX = 0, cameraY = 0;
        let visible = true, destroyed = false, hoveredSystem = -1;
        let editorAction="move", selectedSystem=0, draggingSystem=-1, dragOffset={x:0,z:0};
        const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)").matches;
        const systemLabels = [...root.querySelectorAll("[data-system-index]")];
        const systemComplete = systemLabels.map(label => label.classList.contains("complete"));
        const coreLabel = root.querySelector("[data-city-core-label]");
        let configuration=normalizeConfiguration(rawConfiguration,systemLabels.length);
        let chips=configuration.chips;
        let systems=chips.map(chip => [chip.x,.23,chip.z]);
        let routes=configuration.routes.map(route => route.points);
        let routeColors=configuration.routes.map(route => route.color);

        gl.useProgram(program);
        gl.enable(gl.DEPTH_TEST);
        gl.enable(gl.CULL_FACE);
        gl.cullFace(gl.BACK);
        gl.clearColor(.018, .027, .021, 1);

        gl.bindBuffer(gl.ARRAY_BUFFER, positionBuffer);
        gl.enableVertexAttribArray(attributes.position);
        gl.vertexAttribPointer(attributes.position, 3, gl.FLOAT, false, 0, 0);
        gl.bindBuffer(gl.ARRAY_BUFFER, normalBuffer);
        gl.enableVertexAttribArray(attributes.normal);
        gl.vertexAttribPointer(attributes.normal, 3, gl.FLOAT, false, 0, 0);

        function box(x, y, z, width, height, depth, color, angle = 0, emission = 0) {
            writeModel(model, x, y, z, width, height, depth, angle);
            gl.uniformMatrix4fv(uniforms.model, false, model);
            gl.uniform3fv(uniforms.color, color);
            gl.uniform1f(uniforms.emission, emission);
            gl.drawArrays(gl.TRIANGLES, 0, 36);
        }

        function segment(a, b, width, height, color, emission = 0, y = .015) {
            const dx = b[0] - a[0], dz = b[1] - a[1];
            const length = Math.hypot(dx, dz);
            box((a[0] + b[0]) / 2, y, (a[1] + b[1]) / 2, width, height, length, color, Math.atan2(dx, dz), emission);
        }

        function routeLength(points) {
            let total = 0;
            for (let i = 1; i < points.length; i++) total += Math.hypot(points[i][0]-points[i-1][0], points[i][1]-points[i-1][1]);
            return total;
        }

        function routePoint(points, distance) {
            for (let i = 1; i < points.length; i++) {
                const a = points[i-1], b = points[i];
                const length = Math.hypot(b[0]-a[0], b[1]-a[1]);
                if (distance <= length) {
                    const ratio = length ? distance / length : 0;
                    return { x:a[0]+(b[0]-a[0])*ratio, z:a[1]+(b[1]-a[1])*ratio, angle:Math.atan2(b[0]-a[0],b[1]-a[1]) };
                }
                distance -= length;
            }
            const end = points[points.length-1];
            return { x:end[0], z:end[1], angle:0 };
        }

        function project(point) {
            const x = point[0], y = point[1], z = point[2];
            const clipX = viewProjection[0]*x + viewProjection[4]*y + viewProjection[8]*z + viewProjection[12];
            const clipY = viewProjection[1]*x + viewProjection[5]*y + viewProjection[9]*z + viewProjection[13];
            const clipW = viewProjection[3]*x + viewProjection[7]*y + viewProjection[11]*z + viewProjection[15];
            return { left:(clipX/clipW*.5+.5)*cssWidth, top:(.5-clipY/clipW*.5)*cssHeight };
        }

        function screenToGround(clientX,clientY) {
            const rectangle=canvas.getBoundingClientRect();
            const screenX=clientX-rectangle.left,screenY=clientY-rectangle.top;
            const origin=project([0,0,0]),axisX=project([1,0,0]),axisZ=project([0,0,1]);
            const ax=axisX.left-origin.left,ay=axisX.top-origin.top;
            const bx=axisZ.left-origin.left,by=axisZ.top-origin.top;
            const dx=screenX-origin.left,dy=screenY-origin.top;
            const determinant=ax*by-ay*bx;
            if(Math.abs(determinant)<.0001)return null;
            return {x:(dx*by-dy*bx)/determinant,z:(ax*dy-ay*dx)/determinant};
        }

        function applyConfiguration(raw) {
            configuration=normalizeConfiguration(raw,systemLabels.length);
            chips=configuration.chips;
            systems=chips.map(chip => [chip.x,.23,chip.z]);
            routes=configuration.routes.map(route => route.points);
            routeColors=configuration.routes.map(route => route.color);
            render(performance.now());
        }

        function placeLabels() {
            const compact = cssWidth < 650;
            const systemWidth = compact ? 120 : 170;
            const systemHeight = compact ? 46 : 62;
            const coreWidth = compact ? 104 : 132;
            const coreHeight = compact ? 39 : 48;

            const placeOnPlane = (label, center, worldWidth, worldDepth, elementWidth, elementHeight) => {
                if (!label) return;
                const middle = project(center);
                const xEdge = project([center[0]+worldWidth,center[1],center[2]]);
                const zEdge = project([center[0],center[1],center[2]+worldDepth]);
                const a = (xEdge.left-middle.left)/elementWidth;
                const b = (xEdge.top-middle.top)/elementWidth;
                const c = (zEdge.left-middle.left)/elementHeight;
                const d = (zEdge.top-middle.top)/elementHeight;
                const e = middle.left-a*elementWidth/2-c*elementHeight/2;
                const f = middle.top-b*elementWidth/2-d*elementHeight/2;
                label.style.width=`${elementWidth}px`;
                label.style.height=`${elementHeight}px`;
                label.style.left="0";
                label.style.top="0";
                label.style.transform=`matrix(${a},${b},${c},${d},${e},${f})`;
            };

            systemLabels.forEach((label, index) => {
                const position = systems[index];
                if (!position) return;
                const chip=chips[index];
                placeOnPlane(label,[position[0],.555,position[2]],(chip?.width??3.18)*.78,(chip?.depth??1.92)*.39,systemWidth,systemHeight);
            });
            if (coreLabel)
                placeOnPlane(coreLabel,[configuration.core.x,.725,configuration.core.z+configuration.core.depth*.31],configuration.core.width*.39,configuration.core.depth*.15,coreWidth,coreHeight);
        }

        function drawPins(x, z, width, depth, large = false) {
            const metal = [.23,.285,.245];
            const sideCount = large ? 8 : 5;
            const endCount = large ? 10 : 5;
            const pinLength = large ? .48 : .38;
            const pinWidth = large ? .105 : .095;
            for (let index=0; index<sideCount; index++) {
                const offset = -depth*.38 + index*(depth*.76/(sideCount-1));
                box(x-width/2-pinLength*.38,.13,z+offset,pinLength,.09,pinWidth,metal);
                box(x+width/2+pinLength*.38,.13,z+offset,pinLength,.09,pinWidth,metal);
            }
            for (let index=0; index<endCount; index++) {
                const offset = -width*.4 + index*(width*.8/(endCount-1));
                box(x+offset,.13,z-depth/2-pinLength*.38,pinWidth,.09,pinLength,metal);
                box(x+offset,.13,z+depth/2+pinLength*.38,pinWidth,.09,pinLength,metal);
            }
        }

        function drawSystemChip(position, index) {
            const [x,,z] = position;
            const chip=chips[index];
            const width=chip?.width??3.18, depth=chip?.depth??1.92;
            const baseColor=chip?.color??[.052,.102,.066];
            const hovered = hoveredSystem === index;
            drawPins(x,z,width,depth);
            box(x,.06,z,width+.44,.16,depth+.43,scaleColor(baseColor,.38));
            box(x,.26,z,width,.38,depth,hovered?scaleColor(baseColor,1.36):baseColor);
            box(x,.47,z,width*.925,.075,depth*.88,hovered?scaleColor(baseColor,1.75):scaleColor(baseColor,1.42));
            box(x,.515,z,width*.84,.055,depth*.74,
                hovered?scaleColor(baseColor,2.05):(systemComplete[index]?scaleColor(baseColor,1.8):scaleColor(baseColor,1.55)),0,hovered ? .16 : (systemComplete[index] ? .1 : 0));
            box(x-width*.33,.552,z-depth*.25,.16,.025,.16,
                hovered||systemComplete[index]?[.55,.9,.28]:[.13,.19,.145],0,hovered||systemComplete[index] ? .8 : 0);
        }

        function drawCoreChip() {
            const core=configuration.core;
            const x=core.x,z=core.z,width=core.width,depth=core.depth,color=core.color;
            drawPins(x,z,width,depth,true);
            box(x,.055,z,width+.65,.18,depth+.67,scaleColor(color,.34));
            box(x,.34,z,width,.5,depth,color);
            box(x,.615,z,width*.95,.09,depth*.93,scaleColor(color,1.4));
            box(x,.675,z,width*.9,.055,depth*.87,scaleColor(color,1.72));
            box(x-width*.38,.712,z-depth*.35,.2,.028,.2,[.55,.92,.27],0,.75);

            const road = [.18,.29,.18];
            box(x,.714,z,width*.62,.018,.055,road);
            box(x,.715,z,.055,.018,depth*.6,road);
            box(x-width*.25,.715,z+depth*.18,width*.22,.018,.045,road);
            box(x+width*.24,.715,z-depth*.18,width*.24,.018,.045,road);
        }

        function drawCity() {
            const coreX=configuration.core.x,coreZ=configuration.core.z;
            const buildingColor = [.115,.195,.132];
            const towerColor = [.15,.26,.17];
            const floorLight = [.31,.48,.29];
            const buildings = [
                [-1.62,-.66,.62,.62,1.25],[-.92,-.78,.72,.7,2.45],[-.1,-.58,.62,.62,1.72],
                [.68,-.72,.68,.68,3.1],[1.48,-.5,.52,.52,1.45],[-1.35,.42,.56,.56,1.72],
                [-.58,.52,.7,.66,2.12],[.27,.5,.54,.54,1.35],[1.02,.48,.66,.62,2.32],[1.65,.38,.42,.42,1.02]
            ];
            buildings.forEach((building, index) => {
                const [x,z,width,depth,height] = building;
                box(coreX+x,.712,coreZ+z,width*1.22,.014,depth*1.22,[.018,.032,.022]);
                box(coreX+x,.70+height/2,coreZ+z,width,height,depth,index===3?towerColor:buildingColor,0,index===3?.1:0);
                for (let floor=.48; floor<height-.15; floor+=.52)
                    box(coreX+x,.70+floor,coreZ+z,width*1.012,.017,depth*1.012,floorLight,0,.09);
                box(coreX+x,.715+height,coreZ+z,width*.72,.045,depth*.72,[.36,.52,.35],0,.14);
            });
            box(coreX+.68,4.26,coreZ-.72,.052,.94,.052,[.55,.88,.32],0,.65);
            box(coreX+.68,4.75,coreZ-.72,.14,.08,.14,[.66,1.0,.32],0,1);
        }

        function render(time) {
            cameraX += (pointerX - cameraX) * .045;
            cameraY += (pointerY - cameraY) * .045;
            const aspect = cssWidth/cssHeight;
            const cameraSize=Math.max(6,Math.min(20,configuration.cameraSize||10.4));
            const halfHeight = aspect < 1.1 ? cameraSize*1.31 : cameraSize;
            const halfWidth = halfHeight * aspect;
            const panX = cameraX * .42;
            const panZ = cameraY * .34;
            orthographic(projection,-halfWidth,halfWidth,-halfHeight,halfHeight,.1,100);
            lookAt(view,[
                18+panX,
                18,
                18+panZ
            ],[panX,.25,panZ]);
            multiply(viewProjection,projection,view);
            gl.uniformMatrix4fv(uniforms.projection,false,projection);
            gl.uniformMatrix4fv(uniforms.view,false,view);
            gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);

            // The ground is deliberately much larger than the camera frustum, so the network has no visible edge.
            box(0,-.21,0,72,.38,72,scaleColor(configuration.groundColor,.48));
            box(0,-.005,0,71.5,.025,71.5,configuration.groundColor);

            for (let coordinate=-30; coordinate<=30; coordinate+=1.5) {
                const major = Math.abs(coordinate % 6) < .01;
                const color = scaleColor(configuration.gridColor,major ? .7 : .38);
                const width = major ? .024 : .012;
                box(coordinate,.014,0,width,.012,60,color);
                box(0,.014,coordinate,60,.012,width,color);
            }

            const detailColor = [.055,.105,.068];
            [[[-15,-7.8],[-12.4,-7.8],[-10.2,-5.6]],[[-14,7.4],[-12.2,7.4],[-10.1,5.3]],
             [[14,-7.1],[12.1,-7.1],[9.8,-4.8]],[[15,8.1],[12.6,8.1],[10.1,5.6]],
             [[-5,-10],[-5,-8.8],[-3.8,-7.6]],[[4.5,10],[4.5,8.6],[3.4,7.5]]].forEach(path => {
                for(let i=1;i<path.length;i++) segment(path[i-1],path[i],.018,.018,detailColor);
            });

            routes.forEach((path,index) => {
                const routeColor=scaleColor(routeColors[index]??[.72,.93,.39],.33);
                for(let i=1;i<path.length;i++) segment(path[i-1],path[i],.045,.028,routeColor);
            });

            systems.forEach(drawSystemChip);
            drawCoreChip();
            drawCity();

            const phase = reducedMotion ? .34 : (time*.00016)%1;
            routes.forEach((path,index) => {
                const total = routeLength(path);
                for (let trail=0; trail<3; trail++) {
                    const progress = (phase+index*.23-trail*.027+1)%1;
                    const point = routePoint(path,total*progress);
                    const strength = 1-trail*.27;
                    const routeColor=routeColors[index]??[.72,.93,.39];
                    box(point.x,.125+trail*.003,point.z,.075-trail*.012,.07-trail*.012,.62-trail*.12,
                        scaleColor(routeColor,strength),point.angle,1-trail*.24);
                }
            });

            placeLabels();
        }

        function resize() {
            const rectangle = canvas.getBoundingClientRect();
            cssWidth = Math.max(1,rectangle.width);
            cssHeight = Math.max(1,rectangle.height);
            const ratio = Math.min(devicePixelRatio || 1,1.5);
            const width = Math.round(cssWidth*ratio), height = Math.round(cssHeight*ratio);
            if (canvas.width!==width || canvas.height!==height) {
                canvas.width=width; canvas.height=height;
                gl.viewport(0,0,width,height);
            }
            render(performance.now());
        }

        function tick(time) {
            if (destroyed) return;
            if (visible && time-lastFrame>30) { render(time); lastFrame=time; }
            frame=requestAnimationFrame(tick);
        }

        const resizeObserver = new ResizeObserver(resize);
        const intersectionObserver = new IntersectionObserver(entries => { visible=entries[0]?.isIntersecting!==false; });
        const pointerMove = event => {
            const rectangle=canvas.getBoundingClientRect();
            if(editorMode && draggingSystem>=0) {
                const ground=screenToGround(event.clientX,event.clientY);
                if(!ground)return;
                const chip=chips[draggingSystem];
                const oldX=chip.x,oldZ=chip.z;
                chip.x=ground.x-dragOffset.x;
                chip.z=ground.z-dragOffset.z;
                systems[draggingSystem]=[chip.x,.23,chip.z];
                const route=routes[draggingSystem];
                if(route?.length) {
                    route[0][0]+=chip.x-oldX;
                    route[0][1]+=chip.z-oldZ;
                }
                render(performance.now());
                return;
            }
            pointerX=((event.clientX-rectangle.left)/rectangle.width-.5)*2;
            pointerY=((event.clientY-rectangle.top)/rectangle.height-.5)*2;
            if (reducedMotion) render(performance.now());
        };
        const pointerLeave = () => { pointerX=0; pointerY=0; };
        const contextLost = event => { event.preventDefault(); root.classList.add("webgl-unavailable"); };
        const pointerDown = event => {
            if(!editorMode)return;
            const ground=screenToGround(event.clientX,event.clientY);
            if(!ground)return;
            if(editorAction==="route") {
                dotNetReference?.invokeMethodAsync("AddRoutePointFromScene",selectedSystem,ground.x,ground.z);
                return;
            }
            let nearest=-1,distance=Infinity;
            systems.forEach((position,index) => {
                const value=Math.hypot(position[0]-ground.x,position[2]-ground.z);
                if(value<distance){distance=value;nearest=index;}
            });
            if(nearest<0||distance>3)return;
            selectedSystem=nearest;
            draggingSystem=nearest;
            dragOffset={x:ground.x-chips[nearest].x,z:ground.z-chips[nearest].z};
            canvas.setPointerCapture?.(event.pointerId);
            dotNetReference?.invokeMethodAsync("SelectChipFromScene",nearest);
        };
        const pointerUp = event => {
            if(draggingSystem<0)return;
            const index=draggingSystem,chip=chips[index];
            draggingSystem=-1;
            canvas.releasePointerCapture?.(event.pointerId);
            dotNetReference?.invokeMethodAsync("MoveChipFromScene",index,chip.x,chip.z);
        };
        const labelEnterHandlers = systemLabels.map((label,index) => {
            const enter = () => { hoveredSystem=index; if (reducedMotion) render(performance.now()); };
            const leave = () => { hoveredSystem=-1; if (reducedMotion) render(performance.now()); };
            label.addEventListener("pointerenter",enter);
            label.addEventListener("pointerleave",leave);
            return { label,enter,leave };
        });

        resizeObserver.observe(canvas);
        intersectionObserver.observe(canvas);
        canvas.addEventListener("pointermove",pointerMove,{passive:true});
        canvas.addEventListener("pointerleave",pointerLeave,{passive:true});
        canvas.addEventListener("webglcontextlost",contextLost,false);
        if(editorMode) {
            canvas.addEventListener("pointerdown",pointerDown);
            canvas.addEventListener("pointerup",pointerUp);
            canvas.addEventListener("pointercancel",pointerUp);
        }
        resize();
        if (!reducedMotion) frame=requestAnimationFrame(tick);

        return {
            update(raw) { applyConfiguration(raw); },
            setEditorMode(mode,index) { editorAction=mode==="route"?"route":"move"; selectedSystem=Math.max(0,Number(index)||0); },
            destroy() {
                destroyed=true;
                cancelAnimationFrame(frame);
                resizeObserver.disconnect();
                intersectionObserver.disconnect();
                canvas.removeEventListener("pointermove",pointerMove);
                canvas.removeEventListener("pointerleave",pointerLeave);
                canvas.removeEventListener("webglcontextlost",contextLost);
                canvas.removeEventListener("pointerdown",pointerDown);
                canvas.removeEventListener("pointerup",pointerUp);
                canvas.removeEventListener("pointercancel",pointerUp);
                labelEnterHandlers.forEach(({label,enter,leave}) => {
                    label.removeEventListener("pointerenter",enter);
                    label.removeEventListener("pointerleave",leave);
                });
                gl.deleteBuffer(positionBuffer);
                gl.deleteBuffer(normalBuffer);
                gl.deleteProgram(program);
            }
        };
    }

    window.synapseCity3d = {
        start(canvasId,configuration,editorMode,dotNetReference) {
            const canvas=document.getElementById(canvasId);
            if (!canvas) return false;
            if (scenes.has(canvasId)) return true;
            try {
                const scene=createScene(canvas,configuration,Boolean(editorMode),dotNetReference);
                if (!scene) return false;
                scenes.set(canvasId,scene);
                return true;
            } catch (error) {
                canvas.parentElement?.classList.add("webgl-unavailable");
                console.warn("Synapse 3D city could not start",error);
                return false;
            }
        },
        stop(canvasId) {
            const scene=scenes.get(canvasId);
            if (!scene) return;
            scene.destroy();
            scenes.delete(canvasId);
        },
        update(canvasId,configuration) {
            scenes.get(canvasId)?.update(configuration);
        },
        setEditorMode(canvasId,mode,index) {
            scenes.get(canvasId)?.setEditorMode(mode,index);
        }
    };
})();
