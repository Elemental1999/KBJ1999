#include <stdio.h>
#include <stdlib.h>
#include <pthread.h>
#include <unistd.h>

#define THRESHOLD_FACTOR 2 // Threshold = Total Processes / Stack Nodes

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
    pthread_mutex_lock(&stackLock);

    if (*stack == NULL)
    {
        *stack = (StackNode*)malloc(sizeof(StackNode));
        (*stack)->process_list = process;
        (*stack)->next = NULL;
    }
    else
    {
        if (process->type == FOREGROUND)
        {
            process->next = (*stack)->process_list;
            (*stack)->process_list = process;
        }
        else
        {
            Process* current = (*stack)->process_list;
            if (current == NULL)
            {
                (*stack)->process_list = process;
            }
            else
            {
                while (current->next != NULL)
                {
                    current = current->next;
                }
                current->next = process;
                process->next = NULL;
            }
        }
    }

    pthread_mutex_unlock(&stackLock);
}

Process* pop(StackNode** stack)
{
    pthread_mutex_lock(&stackLock);
    if (*stack == NULL || (*stack)->process_list == NULL)
    {
        pthread_mutex_unlock(&stackLock);
        return NULL;
    }

    StackNode* topNode = *stack;
    Process* process = topNode->process_list;
    topNode->process_list = process->next;
    if (topNode->process_list == NULL)
    {
        *stack = topNode->next;
        free(topNode);
    }

    pthread_mutex_unlock(&stackLock);
    return process;
}

void promote(StackNode** stack)
{
    pthread_mutex_lock(&stackLock);
    StackNode* current = *stack;
    StackNode* previous = NULL;

    while (current != NULL && current->next != NULL)
    {
        previous = current;
        current = current->next;
    }

    if (current != NULL && previous != NULL && current->process_list != NULL)
    {
        Process* headProcess = current->process_list;
        current->process_list = headProcess->next;
        headProcess->next = previous->process_list;
        previous->process_list = headProcess;

        if (current->process_list == NULL)
        {
            previous->next = current->next;
            free(current);
        }
    }

    pthread_mutex_unlock(&stackLock);
}

void split_n_merge(StackNode** stack)
{
    pthread_mutex_lock(&stackLock);
    StackNode* current = *stack;
    int totalProcesses = 0, stackNodes = 0;

    while (current != NULL)
    {
        stackNodes++;
        Process* process = current->process_list;
        while (process != NULL)
        {
            totalProcesses++;
            process = process->next;
        }
        current = current->next;
    }

    if (stackNodes == 0)
    {
        pthread_mutex_unlock(&stackLock);
        return;
    }

    int threshold = totalProcesses / (stackNodes * THRESHOLD_FACTOR);
    int split_occurred = 0;
    do
    {
        split_occurred = 0;
        current = *stack;

        while (current != NULL)
        {
            Process* process = current->process_list;
            int length = 0;

            while (process != NULL)
            {
                length++;
                process = process->next;
            }

            if (length > threshold)
            {
                Process* firstHalf = current->process_list;
                Process* secondHalf = current->process_list;
                Process* prev = NULL;
                for (int i = 0; i < length / 2; i++)
                {
                    prev = secondHalf;
                    secondHalf = secondHalf->next;
                }

                if (prev != NULL) prev->next = NULL;

                StackNode* newNode = (StackNode*)malloc(sizeof(StackNode));
                newNode->process_list = secondHalf;
                newNode->next = current->next;
                current->next = newNode;

                split_occurred = 1;
                break;
            }
            current = current->next;
        }
    } while (split_occurred);

    pthread_mutex_unlock(&stackLock);
}

void printProcess(Process* p)
{
    if (p == NULL) return;

    printf("Process ID: %d ", p->id);
    if (p->type == FOREGROUND)
    {
        printf("Foreground\n");
    }
    else
    {
        printf("Background\n");
    }
}

int main()
{
    pthread_mutex_init(&stackLock, NULL);

    Process* process1 = (Process*)malloc(sizeof(Process));
    process1->id = 0;
    process1->type = FOREGROUND;
    process1->next = NULL;
    push(&stack, process1);

    Process* process2 = (Process*)malloc(sizeof(Process));
    process2->id = 1;
    process2->type = BACKGROUND;
    process2->next = NULL;
    push(&stack, process2);

    promote(&stack);
    split_n_merge(&stack);

    Process* p;
    while ((p = pop(&stack)) != NULL)
    {
        printProcess(p);
        free(p);
    }

    pthread_mutex_destroy(&stackLock);
    return 0;
}