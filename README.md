# RobotLang

RobotLang es un lenguaje de programacion sencillo, escrito en español, diseñado para controlar un brazo robotico mediante scripts de texto.  
El objetivo principal es permitir que cualquier persona con conocimientos basicos de programacion (en futuras actualizaciones para que cualquiera) pueda escribir programas legibles, seguros y faciles de depurar para mover un robot real o simulado.

El proyecto esta implementado en C# y funciona como un interprete que ejecuta instrucciones paso a paso, validando errores de sintaxis y limites fisicos antes de enviar comandos al hardware.

---

## Motivacion del proyecto

La mayoria de ejemplos para brazos roboticos se basan en codigo embebido o APIs complejas.  
RobotLang nace con estas ideas claras:

- usar español para reducir la barrera de entrada
- separar la logica del lenguaje del hardware
- prevenir movimientos peligrosos antes de llegar al robot
- permitir simulacion sin hardware conectado
- mantener el codigo facil de leer y extender

Por eso se eligio un interprete en PC que traduce instrucciones humanas a comandos de bajo nivel para Arduino u otros controladores.

---

## Arquitectura general

El proyecto esta dividido en capas claras:

- app  
  manejo de linea de comandos, carga del archivo y control del ciclo de ejecucion

- language  
  contiene el lenguaje en si: valores, variables, expresiones, evaluador y errores

- robot  
  reglas del brazo, limites fisicos, estado interno e interprete de instrucciones

- transports  
  capa de comunicacion con el robot, ya sea simulada o por puerto serial

Esta separacion permite cambiar el hardware sin modificar el lenguaje, o reutilizar el lenguaje en una interfaz grafica en el futuro.

---

## Como funciona

RobotLang lee un archivo de texto linea por linea.  
Cada linea se analiza, se valida y se ejecuta inmediatamente.

El interprete mantiene un estado interno que incluye:

- variables del programa
- posicion actual de cada articulacion
- configuracion del brazo y sus limites

Antes de mover una articulacion, el sistema valida que el movimiento no exceda rangos seguros.  
Si algo es invalido, la ejecucion se detiene y se muestra un error con linea exacta y causa.

---

## Ejecucion del programa

Desde la carpeta del proyecto:
"dotnet run -- programa.txt"

Para ejecutar contra un Arduino conectado: 
"dotnet run -- programa.txt COM3" (o el COM que asignemos en Arduino IDE)

Si no se especifica un puerto, el sistema usa un modo simulacion que imprime los comandos en consola.

---

## Sintaxis basica del lenguaje.

-COMENTARIOS: Las lineas que empiezan con # son ignoradas.
"#Esto es un comentarios".

DECLARACION DE VARIABLES: 
-entero x = 5;
-cadena nombre = "robot";

ASIGNACION: 
x = x + 1;
nombre = nombre + "lang";

IMPRESION EN CONSOLA: 
Imprimir(x);
Imprimir("hola mundo");

PAUSA MANUAL:
Detiene la ejecucion hasta que el usuario presione enter
Pausar();

---
## Control del brazo robotico

Centrar el brazo:
Lleva todas las articulaciones a su posicion inicial
-Centrar();

Mover una articulacion:
El movimiento es relativo a la posicion actual
Mover(HOMBRO, 10);
Mover(CODO, -5);
Si el movimiento excede los limites permitidos, el programa falla antes de enviar el comando al robot.

Esperar un tiempo
Esperar(500);
El tiempo esta en milisegundos y se valida para evitar valores peligrosos.

---
## Condiciones
Si:
Si x > 5 Entonces
    Imprimir("x es mayor a 5");
FinSi

Mientras:
Mientras x < 10 Hacer
    Imprimir(x);
    x = x + 1;
FinMientras
Los bucles tienen una proteccion contra ciclos infinitos.
---
## Expresiones soportadas
operadores aritmeticos: + - * /
comparaciones: < <= > >= == !=
logicos: and or not
booleanos: true false
concatenacion de cadenas con +

EJEMPLO:

Si (x > 3 and x < 10) Entonces
    Imprimir("en rango");
FinSi

MANEJO DE ERRORES:
Cuando ocurre un error, RobotLang muestra:
-numero de linea
-mensaje descriptivo
-linea original
-indicador visual de la columna

Ejemplo:
Error en línea 7: Articulación desconocida: 'BRAZO'
Esto facilita mucho la depuracion del programa.
---
## Modo simulacion vs modo real

MODO SIMULACION:
--no requiere hardware
--imprime comandos en consola
--ideal para pruebas y aprendizaje

MODO REAL:
--se conecta por puerto serial
--espera confirmacion del Arduino
--valida respuestas OK o ERR
Ambos usan exactamente el mismo lenguaje y scripts.

##Razones del diseño
-lenguaje simple para enseñanza y prototipos
-validacion en PC para proteger el hardware
-separacion clara de responsabilidades
-codigo mantenible y extensible
-preparado para futuras interfaces graficas o web

Posibles extensiones futuras
-soporte para mas articulaciones
-pinza real y sensores
-funciones definidas por el usuario
-interfaz grafica
-comunicacion wifi o bluetooth
-exportar a otros controladores
---
## Estado actual

El lenguaje esta completamente funcional.
El interprete, el evaluador de expresiones y la simulacion estan estables.
La integracion con Arduino por serial esta lista para control real de servomotores.
---
## Licencia
Proyecto educativo y experimental.
Uso libre para aprendizaje, pruebas y extension.
---

## Ejemplos de scripts que sirven y que no:
# ejemplo 1: centrar y mover articulaciones con esperas
Centrar();
Esperar(500);
Mover(HOMBRO, 10);
Esperar(200);
Mover(CODO, -5);
Esperar(200);
Mover(MUÑECA, 15);


# ejemplo 2: bucle controlado con variable y limites seguros
entero i = 0;
Centrar();
Mientras i < 3 Hacer
    Mover(BASE, 5);
    Esperar(150);
    i = i + 1;
FinMientras


# ejemplo 3: condicion y calculo del delta usando expresiones
entero delta = 8;
Centrar();
Si delta >= 1 and delta <= 20 Entonces
    Mover(HOMBRO, delta);
    Esperar(200);
    Mover(HOMBRO, -delta);
FinSi
---
Ejemplos que no funcionan
# ejemplo 1: articulacion desconocida
Centrar();
Mover(BRAZO, 10);


# ejemplo 2: mover fuera de rango (HOMBRO tiene Min=10, empieza en 90, 90 + 200 = 290)
Centrar();
Mover(HOMBRO, 200);


# ejemplo 3: error de sintaxis (falta punto y coma)
entero x = 5
Centrar();
