#include <stdio.h>
#include <stdlib.h>
#include <pthread.h>
#include <unistd.h>

#define THRESHOLD_FACTOR 2 // 임계값 = 전체 프로세스 / 스택 노드

typedef enum {
    FOREGROUND,
    BACKGROUND
}
ProcessType;

typedef struct Process
{
    int id;
    ProcessType type;
    struct Process* next;
    int isPromoted; // 프로모션 여부를 나타내는 플래그
}
Process;

typedef struct StackNode
{
    Process* process_list;
    struct StackNode* next;
}
StackNode;

StackNode* stack = NULL;
pthread_mutex_t stackLock;

void push(StackNode** stack, Process* process)
{
    StackNode* newNode = (StackNode*)malloc(sizeof(StackNode));
    process->next = NULL; // 새 프로세스의 next 포인터 초기화

    if (*stack == NULL)
    {
        newNode->process_list = process;
        newNode->next = NULL;
        *stack = newNode;
    }
    else
    {
        // 새 프로세스를 스택의 최상단 노드의 프로세스 리스트의 맨 앞에 삽입
        process->next = (*stack)->process_list;
        (*stack)->process_list = process;
    }
}

Process* pop(StackNode** stack)
{
    if (*stack == NULL) return NULL;

    Process* process = (*stack)->process_list;
    (*stack)->process_list = process->next;

    if ((*stack)->process_list == NULL)
    {
        StackNode* temp = *stack;
        *stack = (*stack)->next;
        free(temp);
    }

    return process;
}

void promote(StackNode** stack)
{
    pthread_mutex_lock(&stackLock);

    if (*stack == NULL || (*stack)->next == NULL)
    { // 스택에 노드가 없거나 하나만 있는 경우
        pthread_mutex_unlock(&stackLock);
        return;
    }

    // 스택의 마지막 노드를 찾습니다.
    StackNode* current = *stack;
    while (current->next->next != NULL)
    {
        current = current->next;
    }

    // 스택의 마지막 노드의 모든 프로세스를 프로모션합니다.
    Process* processToPromote = current->next->process_list;
    while (processToPromote != NULL)
    {
        processToPromote->isPromoted = 1;
        processToPromote = processToPromote->next;
    }

    // 프로모션된 프로세스 리스트를 스택의 바닥으로 이동합니다.
    StackNode* promotedNode = current->next; // 프로모션된 노드
    current->next = NULL; // 마지막에서 두 번째 노드의 next 포인터를 NULL로 설정

    // 새로운 노드를 스택의 바닥에 추가합니다.
    promotedNode->next = *stack;
    *stack = promotedNode;

    pthread_mutex_unlock(&stackLock);
}

void printProcess(Process* p)
{
    if (p == NULL) return;

    printf("Process ID: %d, Type: %s, Promoted: %s\n",
           p->id,
           p->type == FOREGROUND ? "Foreground" : "Background",
           p->isPromoted ? "Yes" : "No");
}

int main()
{
    pthread_mutex_init(&stackLock, NULL);

    // 예시 프로세스 추가
    Process* process1 = (Process*)malloc(sizeof(Process));
    process1->id = 0;
    process1->type = FOREGROUND;
    process1->next = NULL;
    process1->isPromoted = 0; // 초기값은 0 (프로모션되지 않음)
    push(&stack, process1);

    // 프로모션 기능 테스트
    promote(&stack);

    Process* p;
    while ((p = pop(&stack)) != NULL)
    {
        printProcess(p);
        free(p);
    }

    pthread_mutex_destroy(&stackLock);
    return 0;
}